using System;
using System.Collections.Generic;
using System.Text;
using Leadtools.Codecs;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Recognition;
using Leadtools.Ocr;
using Leadtools.Forms.Common;
using Leadtools.Forms.Recognition.Barcode;
using Leadtools.Forms.Recognition.Ocr;
using Leadtools.Barcode;
using Leadtools.ImageProcessing.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Net.Mime;
using System.Security.Permissions;
using System.ServiceModel.Security;
using System.Xml;
using System.Xml.Linq;
using Leadtools;
using Leadtools.Forms.Commands.Internal;
using Leadtools.Forms.Processing.Omr.Fields;
using Leadtools.ImageProcessing;
using Newtonsoft.Json.Linq;

namespace ElevationCertificateSlicer
{
   public class ImageInfo
   {
      public FileInfo ImageFileInfo;
      public RasterImage Image;
   }
   public class OcrImageInfo
   {
      public ImageInfo InitialImage;
      public ImageInfo CenteredImage;
      public ImageInfo OmrImage;
   }


   /// <summary>
   /// OcrMaster run Ocr on forms
   /// </summary>
   public class OcrMaster
   {
      public string BaseFolder =
         System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
      public IOcrEngine OcrEngine;
      public List<MasterForm> BlockMasterForms = new List<MasterForm>();
      public List<MasterForm> MasterForms = new List<MasterForm>();
      public IOcrEngine CleanUpOcrEngine;
      public OcrEngineType EngineType = OcrEngineType.OmniPage;
      public FormRecognitionEngine RecognitionEngine;
      public FormProcessingEngine ProcessingEngine;
      public RasterCodecs RasterCodecs;
      public OcrEngineType OcrEngineType;
      private Stopwatch _recognitionTimer = new Stopwatch();
      private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
     /// <summary>
      /// OcrMaster load master forms and start engines
      /// </summary>
      public OcrMaster()
      {
         string[] masterFormAttributes = Directory.GetFiles(BaseFolder, "2*.bin", SearchOption.AllDirectories);

         if (!StartUpEngines())
         {
            throw new Exception("Could not start engines");
         }
         foreach (string masterFormAttribute in masterFormAttributes)
         {
            if (@masterFormAttribute.Contains("_runtime.")) continue;
            logger.Info($"Loading master form {masterFormAttribute}");
            string fieldsfName = String.Concat(Path.GetFileNameWithoutExtension(masterFormAttribute), ".xml");
            string fieldsfullPath = Path.Combine(Path.GetDirectoryName(masterFormAttribute), fieldsfName);
            var currentMasterForm = LoadMasterForm(masterFormAttribute, fieldsfullPath);

            var formList = MasterForms;
            if (fieldsfName.Contains("Block")) formList = BlockMasterForms;
            formList.Add(currentMasterForm);
            string imageName = String.Concat(Path.GetFileNameWithoutExtension(masterFormAttribute), ".tif");
            string imagefullPath = Path.Combine(Path.GetDirectoryName(masterFormAttribute), imageName);
            var image = formList[formList.Count - 1].Image =
               RasterCodecs.Load(imagefullPath, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
            // RasterCodecs.Load(imagefullPath, 0, CodecsLoadByteOrder.BgrOrGrayOrRomm, 1, -1);
            FormRecognitionAttributes masterFormAttributes2 = RecognitionEngine.CreateMasterForm(masterFormAttribute, Guid.Empty, null);
            for (int i = 0; i < image.PageCount; i++)
            {
               image.Page = i + 1;
               //Add the master form page to the recognition engine 
               RecognitionEngine.AddMasterFormPage(masterFormAttributes2, image, null);
            }
            //Close the master form and save it's attributes 
            RecognitionEngine.CloseMasterForm(masterFormAttributes2);
            //formList[formList.Count - 1].Attributes = masterFormAttributes2;

         }
      }

      public List<FilledForm> ProcessOcr(string outDir, List<ImageInfo> fileInfos)
      {
         var retForms = new List<FilledForm>();
         var usedMasters = new HashSet<MasterForm>();
         Stopwatch stopWatch = new Stopwatch();
         stopWatch.Start();
         foreach (var ofi in fileInfos)
         {

            FilledForm newForm = new FilledForm();
            retForms.Add(newForm);
            newForm.ImageInfoMaster.InitialImage = ofi;
            newForm.Name = Path.GetFileNameWithoutExtension(ofi.ImageFileInfo.Name);
            if (ofi.Image == null)
            {
               ofi.Image = LoadImageFile(ofi.ImageFileInfo.FullName, 1, -1);
            }
            FormRecognitionAttributes filledFormAttributes = RecognitionEngine.CreateForm(null);
            ofi.Image.ChangeViewPerspective(RasterViewPerspective.TopLeft);
            for (int i = 0; i < ofi.Image.PageCount; i++)
            {
               ofi.Image.Page = i + 1;
               //Add each page of the filled form to the recognition engine 
               RecognitionEngine.AddFormPage(filledFormAttributes, ofi.Image, null);
            }
            
            RecognitionEngine.CloseForm(filledFormAttributes);

            CreateFormForRecognition(newForm, FormsRecognitionMethod.Complex);
            List<FormRecognitionResult> results = new List<FormRecognitionResult>();
            MasterForm currentMasterBlockForm = null;
            int currentConfidence = 70;
            foreach(var master in BlockMasterForms)
            {
               if (usedMasters.Contains(master))
                  continue;
               var result = RecognitionEngine.CompareForm(master.Attributes, filledFormAttributes, null, null);
               logger.Debug($"Check {master} for {newForm} {stopWatch.ElapsedMilliseconds} {result.Confidence}");
               if (result.Confidence > currentConfidence)
               {
                  currentMasterBlockForm = master;
                  currentConfidence = result.Confidence;
               }
            }
            if (currentMasterBlockForm != null)
            {
               logger.Info($"FilledForm matched {newForm.Name} {newForm.Status} {stopWatch.ElapsedMilliseconds} ");
               newForm.ImageInfoMaster.InitialImage = ofi;
               var centeredImage = ofi.Image.CloneAll();
               
               CleanupImage(centeredImage);
               newForm.ImageInfoMaster.CenteredImage = new ImageInfo() {Image = centeredImage};
               var omrImage = centeredImage.CloneAll();
               PrepareOmrImage(omrImage);
               newForm.ImageInfoMaster.OmrImage = new ImageInfo() { Image = omrImage };
               newForm.Status = "Matched";
               newForm.Master = currentMasterBlockForm;
               var fields = currentMasterBlockForm.ProcessingPages[0];
               foreach (var field in fields)
               {
                  int fudge = 10;
                  var rect200 = field.Bounds;
                  var rect300 = new LeadRect(rect200.Left * 3 / 2 - fudge, rect200.Top * 3 / 2 - fudge, rect200.Width * 3 / 2 + fudge,
                     rect200.Height * 3 / 2 + fudge);
                  var image = newForm.ImageInfoMaster.CenteredImage.Image.CloneAll();
                  var isBlock = field.Name.Contains("block");
                  var subDir = Path.Combine(outDir, isBlock ? "blocks" : "fields");
                  EnsurePathExists(subDir);
                  var fileName = Path.Combine(subDir, newForm.Name + "_" + field.Name + ".jpg");
                  
                  CropCommand command = new CropCommand
                  {
                     Rectangle = rect300
                  };
                  command.Run(image);
                  RasterCodecs.Save(image, fileName, RasterImageFormat.Jpeg, bitsPerPixel: 8);
                  var imageInfo = new ImageInfo() {Image = image, ImageFileInfo = new FileInfo(fileName)};
                  var imageField = new ImageField() {Field = field, ImageInfo = imageInfo};
                  if (!isBlock && field.GetType() == typeof(TextFormField))
                  {
                     using (IOcrPage ocrPage = OcrEngine.CreatePage(image, OcrImageSharingMode.AutoDispose))
                     {
                        OcrZone ocrZone = new OcrZone();
                        ocrZone.ZoneType = OcrZoneType.Text;
                        ocrZone.Bounds = new LeadRect(0,0, image.ImageSize.Width, image.ImageSize.Height);
                        ocrPage.Zones.Add(ocrZone);

                        ocrPage.AutoZone(null);
                        ocrPage.Recognize(null);
                        var resultsPage = GetPageConfidence(ocrPage);
                        imageField.Confidence = resultsPage.Confidence;
                        logger.Info($"field ");
                     }
                  }
                  newForm.ImageFields.Add(imageField);
               }
               usedMasters.Add(currentMasterBlockForm);
            }
            logger.Info($"FilledForm processed {newForm.Name} {newForm.Status} {stopWatch.ElapsedMilliseconds} ");
            if (usedMasters.Count == BlockMasterForms.Count)
            {
               logger.Info("found all master forms");
               break;
            }
         }
         stopWatch.Stop();

         return retForms;
      }

      private void PrepareOmrImage(RasterImage omrImage)
      {
         var colorResolution =
            new ColorResolutionCommand
            {
               BitsPerPixel = 1,
               DitheringMethod = Leadtools.RasterDitheringMethod.None,
               PaletteFlags = Leadtools.ImageProcessing.ColorResolutionCommandPaletteFlags.Fixed
            };

         colorResolution.Run(omrImage);
         LineRemoveCommand(LineRemoveCommandType.Horizontal, omrImage);
         LineRemoveCommand(LineRemoveCommandType.Vertical, omrImage);
      }

      public void CleanupImage(RasterImage imageToClean)
      {
         imageToClean.Page = 1;

         DeskewCommand deskewCommand = new DeskewCommand();
         if (imageToClean.Height > 3500)
            deskewCommand.Flags = DeskewCommandFlags.DocumentAndPictures |
                                  DeskewCommandFlags.DoNotPerformPreProcessing |
                                  DeskewCommandFlags.UseNormalDetection |
                                  DeskewCommandFlags.DoNotFillExposedArea;
         else
            deskewCommand.Flags = DeskewCommandFlags.DeskewImage | DeskewCommandFlags.DoNotFillExposedArea;
         deskewCommand.Run(imageToClean);
      }
      public void LineRemoveCommand(LineRemoveCommandType type, RasterImage image)
      {
         LineRemoveCommand command = new LineRemoveCommand();
         command.Type = type;
         command.Flags = LineRemoveCommandFlags.RemoveEntire;
         command.MaximumLineWidth = 6;
         command.MinimumLineLength = 30;
         command.MaximumWallPercent = 10;
         command.Wall = 10;
         command.Run(image);
      }


      public void CreateFormForRecognition(FilledForm form, FormsRecognitionMethod method)
      {
         form.Attributes = CreateForm(method);
         var image = form.GetImage().CloneAll();
         //int saveCurrentPageIndex = image.Page;
         
         for (int i = 0; i < image.PageCount; i++)
         {
            image.Page = i + 1;//page index is a 1-based starts from 1 not zero

            PageRecognitionOptions pageOptions = new PageRecognitionOptions();
            pageOptions.UpdateImage = true;
            pageOptions.PageType = FormsPageType.Normal;
            AddPageToForm(image, form.Attributes, pageOptions);
         }
      }
      public void AddPageToForm(RasterImage image, FormRecognitionAttributes attributes, PageRecognitionOptions options)
      {
         RecognitionEngine.OpenForm(attributes);
         RecognitionEngine.AddFormPage(attributes, image, options);
         RecognitionEngine.CloseForm(attributes);
      }
      public void AlignForm(FilledForm form, bool calculateAlignment)
      {
         if (calculateAlignment)
         {

            CreateFormForRecognition(form, FormsRecognitionMethod.Complex);

            form.Alignment = RecognitionEngine.GetFormAlignment(form.Master.Attributes, form.Attributes, null);
         }
         else
         {
            form.Alignment = new List<PageAlignment>();
            for (int i = 0; i < form.Result.PageResults.Count; i++)
               form.Alignment.Add(form.Result.PageResults[i].Alignment);
         }
      }


      public FormRecognitionAttributes CreateForm(FormsRecognitionMethod method)
      {
         FormRecognitionOptions options = new FormRecognitionOptions();
         options.RecognitionMethod = method;
         FormRecognitionAttributes attributes = RecognitionEngine.CreateForm(options);
         RecognitionEngine.CloseForm(attributes);
         return attributes;
      }


      public RasterImage LoadImageFile(string fileName, int firstPage, int lastPage)
      {
         // Load the image 
         Stopwatch stopWatch = new Stopwatch();
         stopWatch.Start();
         RasterImage image = RasterCodecs.Load(fileName, 0, CodecsLoadByteOrder.Bgr, firstPage, lastPage);
         stopWatch.Stop();
         logger.Info($"Image Loaded {fileName} {stopWatch.ElapsedMilliseconds} ");
         return image;
      }


      public void ShowError(Exception exp)
      {
         logger.Error(exp);
         logger.Error(exp.StackTrace);
      }

      private bool StartUpEngines()
      {
         try
         {
            RecognitionEngine = new FormRecognitionEngine();
            ProcessingEngine = new FormProcessingEngine();
            StartUpRasterCodecs();
            StartUpOcrEngine();
            //StartUpBarcodeEngine();
            //StartupTwain();
            return true;
         }
         catch(Exception ex)
         {
            return false;
         }
      }

      private void StartUpOcrEngine()
      {
         try
         {
            
            OcrEngine = OcrEngineManager.CreateEngine(EngineType, true);
            OcrEngine.Startup(null, null, null, null);

            CleanUpOcrEngine = OcrEngineManager.CreateEngine(EngineType, true);
            CleanUpOcrEngine.Startup(null, null, null, null);
            //Add an OCRObjectManager to the recognition engines 
            //ObjectManager collection 
            OcrObjectsManager ocrObjectsManager = new OcrObjectsManager(OcrEngine);
            ocrObjectsManager.Engine = OcrEngine;
            RecognitionEngine.ObjectsManagers.Add(ocrObjectsManager);

         }
         catch (Exception exp)
         {
            ShowError(exp);
            throw;
         }
      }

      private void StartUpRasterCodecs()
      {
         try
         {
            RasterCodecs = new RasterCodecs();

            //To turn off the dithering method when converting colored images to 1-bit black and white image during the load
            //so the text in the image is not damaged.
            RasterDefaults.DitheringMethod = RasterDitheringMethod.None;

            //To ensure better results from OCR engine, set the loading resolution to 300 DPI 
            RasterCodecs.Options.Load.Resolution = 300;
            RasterCodecs.Options.RasterizeDocument.Load.Resolution = 300;
         }
         catch (Exception exp)
         {
            ShowError(exp);
            throw;
         }
      }

      private class PageResults
      {
         public double PageConfidence { get; set; }
         public double Confidence { get; set; }
         public int CertainWords { get; set; }
         public int TotalWords { get; set; }

         public PageResults(double pageConfidence, int certainwords, int totalWords)
         {
            PageConfidence = pageConfidence;
            CertainWords = certainwords;
            TotalWords = totalWords;
            Confidence = 0.25 * PageConfidence + 0.75 * certainwords * 100 / totalWords;
         }
      }

      private PageResults GetPageConfidence(IOcrPage ocrPage)
      {
         IOcrPageCharacters pageCharacters = ocrPage.GetRecognizedCharacters();
         double pageConfidence = 0;
         int certainWords = 0;
         int totalWords = 0;
         int totalZoneWords = 0;
         int textZoneCount = 0;

         for (int i = 0; i < ocrPage.Zones.Count; i++)
         {
            IOcrZoneCharacters zoneCharacters = pageCharacters.FindZoneCharacters(i);
            if (zoneCharacters.Count == 0)
               continue;

            textZoneCount++;
            double zoneConfidence = 0;
            int characterCount = 0;
            double wordConfidence = 0;
            totalZoneWords = 0;
            bool newWord = true;
            foreach (var ocrCharacter in zoneCharacters)
            {
               if (newWord)
               {
                  wordConfidence = 0;
                  characterCount = 0;
                  wordConfidence = 1000;
               }
               if (ocrCharacter.Confidence < wordConfidence)
                  wordConfidence = ocrCharacter.Confidence;
               characterCount++;

               if ((ocrCharacter.Position & OcrCharacterPosition.EndOfWord) == OcrCharacterPosition.EndOfWord || (ocrCharacter.Position & OcrCharacterPosition.EndOfLine) == OcrCharacterPosition.EndOfLine)
               {
                  if (characterCount > 3)
                  {
                     if (ocrCharacter.WordIsCertain)
                        certainWords++;
                     totalWords++;
                     totalZoneWords++;
                     zoneConfidence += wordConfidence;
                  }

                  newWord = true;
               }
               else
                  newWord = false;
            }

            if (totalZoneWords > 0)
            {
               zoneConfidence /= totalZoneWords;
               pageConfidence += zoneConfidence;
            }
            else
            {
               zoneConfidence = 0;
               pageConfidence += zoneConfidence;
            }
         }
         if (textZoneCount > 0)
            pageConfidence /= textZoneCount;
         else
            pageConfidence = 0;

         PageResults results = new PageResults(pageConfidence, certainWords, totalWords);
         return results;
      }

      public MasterForm LoadMasterForm(string attributesFileName, string fieldsFileName)
      {
         FormProcessingEngine tempProcessingEngine = new FormProcessingEngine();
         tempProcessingEngine.OcrEngine = OcrEngine;
         //tempProcessingEngine.BarcodeEngine = null;

         MasterForm form = new MasterForm();

         if (File.Exists(attributesFileName))
         {
            byte[] formData = File.ReadAllBytes(attributesFileName);



            form.Attributes.SetData(formData);
            form.Properties = RecognitionEngine.GetFormProperties(form.Attributes);
         }

         if (File.Exists(fieldsFileName))
         {
            tempProcessingEngine.LoadFields(fieldsFileName);
            form.ProcessingPages = tempProcessingEngine.Pages;
         }

         return form;
      }

      public static void EnsurePathExists(string path)
      {
         // ... Set to folder path we must ensure exists.
         try
         {
            // ... If the directory doesn't exist, create it.
            if (!Directory.Exists(path))
            {
               Directory.CreateDirectory(path);
            }
         }
         catch (Exception)
         {
            // Fail silently.
         }
      }

#if SAVEFIELDS
      public void SaveFields(FilledForm form, List<OcrField> ocrFields, bool doOmr = false)
      {
         var sb = new StringBuilder();
         sb.Append($"OMR={doOmr}\n");
         sb.Append($"FormName: {form.Name}\n");
         sb.Append($"FileName: {form.FileName}\n");
         sb.Append(form.Master.Properties.Name + "\n");
         var fi = new FileInfo(form.FileName);
         Debug.Assert(fi.DirectoryName != null, "fi.DirectoryName != null");
         var outDir = Path.Combine(fi.DirectoryName, "data");
         EnsurePathExists(outDir);
         var fiMaster = Path.GetFileNameWithoutExtension(form.Master.Properties.Name);
         var baseMergedName = Path.GetFileNameWithoutExtension(form.FileName) + "~" + fiMaster;
         var jsonMergedName = Path.Combine(outDir, baseMergedName + "_merged.json");

         var baseName = Path.GetFileNameWithoutExtension(form.FileName) + (doOmr ? "_omr" : "_ocr") + "~" + fiMaster;
         var image = doOmr ? form.OmrImage : form.Image;
         Debug.Assert(outDir != null, "outDir != null");
         var imageName = Path.Combine(outDir, baseName + ".png");
         var jsonName = Path.Combine(outDir, baseName + ".json");
         var jsonName2 = Path.Combine(outDir, baseName + "_short.json");

         var fieldInfo = Path.Combine(outDir, baseName + ".field_info");
         sb.Append($"imageName: {imageName}\n");
         sb.Append($"jsonName: {jsonName}\n");
         sb.Append($"jsonName2: {jsonName2}\n");
         sb.Append($"fieldInfo: {fieldInfo}\n");

         RasterCodecs.Save(image, imageName, RasterImageFormat.Png, 1);

         if (form.ProcessingPages?[0] != null)
         {
            var fields = new FormField[form.ProcessingPages[0].Count];
            int i = 0;
            foreach (var field in form.ProcessingPages[0])
            {
               fields[i++] = field;
            }

            sb.Append($"Json: {jsonName}");
            var json = JsonConvert.SerializeObject(fields, Formatting.Indented);
            File.WriteAllText(jsonName, json);
            sb.Append(json);
            OcrRecord curRecord = new OcrRecord();
            curRecord.AppID = AppID;
            curRecord.MasterForms.Add(form.Master.Properties.Name);
            sb = new StringBuilder();
            var jArray = Newtonsoft.Json.Linq.JArray.Parse(json);
            //Debug.WriteLine(jArray.Count);
            foreach (var jToken in jArray)
            {
               var name = jToken.Value<string>("Name");
               var bounds = jToken.Value<string>("Bounds");
               var res = jToken["ResultDefault"];
               if (res != null)
               {
                  if (!doOmr)
                  {
                     var f = new OcrField(name, "text", bounds, res);
                     Debug.Assert(curRecord != null, nameof(curRecord) + " != null");
                     ocrFields.Add(f);
                     curRecord.AddField(f);
                  }
               }
               else
               {
                  if (doOmr)
                  {
                     res = jToken["Result"];
                     var f = new OcrField(name, "omr", bounds, res);
                     Debug.Assert(curRecord != null, nameof(curRecord) + " != null");
                     ocrFields.Add(f);
                     curRecord.AddField(f);
                  }
               }
            }
            File.WriteAllText(jsonName2,
                  JsonConvert.SerializeObject(curRecord, Formatting.Indented));
            var fieldsSort = from o in ocrFields orderby o.VariableName select o;
            File.WriteAllText(jsonMergedName,
               JsonConvert.SerializeObject(fieldsSort.ToArray(), Formatting.Indented));

         }
         else
         {
            sb.Append("Json: No Fields found\n");
         }
         File.WriteAllText(fieldInfo, sb.ToString());
         logger.Info(sb.ToString());
      }
#endif

   }
}
