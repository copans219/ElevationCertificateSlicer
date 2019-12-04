using System;
using System.Collections.Generic;
using Leadtools.Codecs;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Recognition;
using Leadtools.Ocr;
using Leadtools.Forms.Common;
using Leadtools.ImageProcessing.Core;
using Leadtools.ImageProcessing.Effects;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Leadtools;
using Leadtools.ImageProcessing;
using System.Threading;
using Newtonsoft.Json;

namespace ElevationCertificateSlicer
{
   /// <summary>
   /// OcrMaster run Ocr on forms
   /// </summary>
   public partial class OcrMaster
   {
      public int PageTimeoutInSeconds = 30;
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
      public char FilledChar;
      public char UnfilledChar;

      public OcrEngineType OcrEngineType;
      private Stopwatch _recognitionTimer = new Stopwatch();
      private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
     /// <summary>
      /// OcrMaster load master forms and start engines
      /// </summary>
      public OcrMaster()
      {
         string[] masterFormFields = Directory.GetFiles(BaseFolder, "*_Blocks_*.xml", SearchOption.AllDirectories);

         if (!StartUpEngines())
         {
            throw new Exception("Could not start engines");
         }
 
         foreach (string masterFormField in masterFormFields)
         {
            if (masterFormField.Contains("_runtime.")) continue;
            string binFile = String.Concat(Path.GetFileNameWithoutExtension(masterFormField), ".bin");
            RasterImage tifImage = null;
            if (!File.Exists(binFile))
            {
               string imageName = String.Concat(Path.GetFileNameWithoutExtension(masterFormField), ".png");
               string imagefullPath = Path.Combine(Path.GetDirectoryName(masterFormField), imageName);
               tifImage =
                  RasterCodecs.Load(imagefullPath, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
               // RasterCodecs.Load(imagefullPath, 0, CodecsLoadByteOrder.BgrOrGrayOrRomm, 1, -1);
               FormRecognitionAttributes masterFormAttributes2 = RecognitionEngine.CreateMasterForm(masterFormField, Guid.Empty, null);
               for (int i = 0; i < tifImage.PageCount; i++)
               {
                  tifImage.Page = i + 1;
                  //Add the master form page to the recognition engine 
                  RecognitionEngine.AddMasterFormPage(masterFormAttributes2, tifImage, null);
               }
               //Close the master form and save it's attributes 
               RecognitionEngine.CloseMasterForm(masterFormAttributes2);
               //Load the master form image 
               File.WriteAllBytes(binFile, masterFormAttributes2.GetData());
               //binFiles.Add(binFile);
            }
            logger.Info($"Loading master form {masterFormField}");
            
            
            var currentMasterForm = LoadMasterForm(binFile, masterFormField);

            var formList = BlockMasterForms;
            //if (fieldsfName.Contains("Block")) formList = BlockMasterForms;
            formList.Add(currentMasterForm);
         }
      }

     public class FormThreadCallParams
     {
        public ImageInfo ImageInfo;
        public Stopwatch StopWatch;
        public FilledForm Form;
        public FormRecognitionAttributes Attributes;

     }

     public void PrepareNewFormThreader(object obj)
     {
        var pars = (FormThreadCallParams)obj;
        pars.Attributes = PrepareNewForm(pars.ImageInfo, pars.StopWatch, pars.Form);
     }
     public List<FilledForm> ProcessOcr(ResultsForPrettyJson formResults, 
         List<ImageInfo> fileInfos, bool useS3)
      {
         try
         {
            var outDir = formResults.OriginalDirectoryName;
            var retForms = new List<FilledForm>();
            var usedMasters = new HashSet<MasterForm>();
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            formResults.PagesInPdf = fileInfos.Count;
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

               //CleanupImage(ofi.Image);
               var par = new FormThreadCallParams()
               {
                  ImageInfo = ofi, StopWatch = stopWatch, Form = newForm
               };
               if (PageTimeoutInSeconds < 50)
               {
                  Thread t = new Thread(this.PrepareNewFormThreader);
                  t.Start(par);
                  if (!t.Join(TimeSpan.FromSeconds(PageTimeoutInSeconds)))
                  {
                     t.Abort();
                     formResults.TimedOutPages.Add(newForm.Name);
                     formResults.BestFormConfidence.Add(-1);
                     if (formResults.TimedOutPages.Count > 2 && formResults.PagesMappedToForm == 0)
                     {
                        formResults.Status =
                           $"Form abandoned for timeout after {formResults.BestFormConfidence.Count} pages";
                        logger.Error(formResults.Status);
                        return retForms;
                     }

                     continue;
                  }
               }
               else
               {
                  PrepareNewFormThreader(par);
               }

               Debug.Assert(par.Attributes != null);
               var filledFormAttributes = par.Attributes;
               //List<FormRecognitionResult> results = new List<FormRecognitionResult>();
               MasterForm currentMasterBlockForm = null;
               int bestConfidence = -1;
               int currentConfidence = 85;
               foreach (var master in BlockMasterForms)
               {
                  if (usedMasters.Contains(master))
                     continue;
                  var result = RecognitionEngine.CompareForm(master.Attributes, filledFormAttributes, null, null);
                  //logger.Debug($"Check {master} for {newForm} {stopWatch.ElapsedMilliseconds} {result.Confidence}");
                  if (result.Confidence > currentConfidence)
                  {
                     currentMasterBlockForm = master;
                     bestConfidence = currentConfidence = result.Confidence;
                  }
                  else if (result.Confidence > bestConfidence)
                     bestConfidence = result.Confidence;
               }

               formResults.BestFormConfidence.Add(bestConfidence);
               if (currentMasterBlockForm != null)
               {
                  formResults.MasterFormPages.Add(currentMasterBlockForm.Properties.Name);
                  formResults.PagesMappedToForm++;
                  logger.Info($"FilledForm matched {newForm.Name} {newForm.Status} {stopWatch.ElapsedMilliseconds} ");
                  newForm.ImageInfoMaster.InitialImage = ofi;
                  var centeredImage = ofi.Image.CloneAll();

                  CleanupImage(centeredImage);
                  newForm.ImageInfoMaster.CenteredImage = new ImageInfo() {Image = centeredImage};
                  var omrImage = centeredImage.CloneAll();
                  PrepareOmrImage(omrImage);
                  newForm.ImageInfoMaster.OmrImage = new ImageInfo() {Image = omrImage};
                  newForm.Status = "Matched";
                  newForm.Master = currentMasterBlockForm;
                  var alignment =
                     RecognitionEngine.GetFormAlignment(newForm.Master.Attributes, newForm.Attributes, null);
                  var fields = currentMasterBlockForm.ProcessingPages[0];
                  var scaler = currentMasterBlockForm.Resolution;
                  var fieldsOnlyImage = RasterImage.Create(centeredImage.Width, centeredImage.Height,
                     centeredImage.BitsPerPixel, 300, RasterColor.White);
                  //fieldsOnlyImage  = new RasterImage(RasterMemoryFlags.Conventional, centeredImage.Width, centeredImage.Height, centeredInage.BitsPerPixel, RasterByteOrder.Rgb, RasterViewPerspective.TopLeft, null, null, 0);

                  var subDirField = Path.Combine(outDir, "fields");
                  var fileNameFieldOnly = Path.Combine(subDirField, newForm.Name + "_fields.png");
                  var googleResultsFile = Path.Combine(subDirField, newForm.Name + "_google.json");
                  var combined = false;
                  foreach (var field in fields)
                  {
                     var isBlock = field.Name.Contains("block");
                     var rect200 = alignment[0].AlignRectangle(field.Bounds);
                     scaler = 300;
                     int fudge = isBlock ? 30 : 1;
                     var rect300 = new LeadRect(rect200.Left * 300 / scaler - fudge, rect200.Top * 300 / scaler - fudge,
                        rect200.Width * 300 / scaler + fudge,
                        rect200.Height * 300 / scaler + fudge);
                     try
                     {
                        var imageInfoToUse = newForm.ImageInfoMaster.CenteredImage;
                        var zoneType = OcrZoneType.Text;
                        if (field.GetType() == typeof(OmrFormField))
                        {
                           imageInfoToUse = newForm.ImageInfoMaster.OmrImage;
                           zoneType = OcrZoneType.Omr;
                        }
                        else if (field.GetType() == typeof(ImageFormField))
                           zoneType = OcrZoneType.Graphic;

                        var image = imageInfoToUse.Image.CloneAll();
                        var subFolder = isBlock ? "blocks" : "fields";
                        var subDir = Path.Combine(outDir, subFolder);
                        var imageFileName = newForm.Name + "_" + field.Name + ".png";
                        var fileName = Path.Combine(subDir, imageFileName );
                        var imageField = new ImageField
                        {
                           Field = field,
                           FieldResult =
                           {
                              FieldName = field.Name,
                              IsBlock = isBlock,
                              ImageFile = fileName,
                              Bounds = rect300.ToString(),
                              FieldType = zoneType.ToString(),

                              Error = "None"
                           }
                        };
                        imageField.Rectangle = new Rectangle( rect300.X,rect300.Y, rect300.Width, rect300.Height);

                        try
                        {
                           EnsurePathExists(subDir);
                           CropCommand command = new CropCommand
                           {
                              Rectangle = rect300
                           };
                           command.Run(image);
                           if (isBlock)
                           {
                              RasterCodecs.Save(image, fileName, RasterImageFormat.Png, bitsPerPixel: 8);
                              formResults.S3FilesToCopy.Add($"{subFolder}/{imageFileName}");
                           }
                           if (!isBlock && zoneType == OcrZoneType.Text && !combined)
                           {
                              try
                              {
                                 ;
                                 var combiner = new CombineCommand();
                                 //combiner.DestinationImage = fieldsOnlyImage;
                                 combiner.SourceImage = image.Clone();
                                 combiner.DestinationRectangle = rect300;
                                 var regionBounds = image.GetRegionBounds(null);
                                 combiner.SourcePoint = new LeadPoint(regionBounds.X, regionBounds.Y); 
                                 //combiner.Flags = CombineCommandFlags.OperationAdd | CombineCommandFlags.Destination0 | CombineCommandFlags.Source1 | CombineCommandFlags.Destination0 ;

                                 combiner.Flags = CombineCommandFlags.OperationOr | CombineCommandFlags.Destination0; ; // |CombineFastCommandFlags.OperationAverage;
                                 combiner.Run(fieldsOnlyImage);
                                 //combined = true;
                              }
                              catch (Exception exCombine)
                              {
                                 logger.Error(exCombine, $"error combining field {field.Name} {rect300}");
                              }
                           }

                           var imageInfo = new ImageInfo() {Image = image, ImageFileInfo = new FileInfo(fileName)};
                           imageField.ImageInfo = imageInfo;

                           if (!isBlock && zoneType != OcrZoneType.Graphic)
                           {
                              using (IOcrPage ocrPage = OcrEngine.CreatePage(image, OcrImageSharingMode.AutoDispose))
                              {
                                 OcrZone ocrZone = new OcrZone
                                 {
                                    ZoneType = zoneType,
                                    Bounds = new LeadRect(fudge, fudge, image.ImageSize.Width - fudge,
                                       image.ImageSize.Height - fudge)
                                 };
                                 ocrPage.Zones.Add(ocrZone);

                                 ocrPage.Recognize(null);
                                 if (zoneType == OcrZoneType.Omr)
                                 {
                                    GetOmrReading(ocrPage, field, imageField);
                                 }
                                 else if (zoneType == OcrZoneType.Text)
                                 {
                                    var resultsPage = GetPageConfidence(ocrPage);
                                    imageField.FieldResult.Confidence = resultsPage.Confidence;
                                    char[] crlf = {'\r', '\n'};
                                    imageField.FieldResult.Text = ocrPage.GetText(0).TrimEnd(crlf);
                                 }
                              }
                           }

                           logger.Info(
                              $"field {field.Name} {rect300} [{imageField.FieldResult.Text}] confidence: {imageField.FieldResult.Confidence}");
                        }
                        catch (Exception exField)
                        {
                           logger.Error(exField, $"Error processing {field.Name}");
                           formResults.FieldsWithError++;
                           imageField.FieldResult.Error = exField.Message;
                        }

                        newForm.ImageFields.Add(imageField);
                        formResults.OcrFields.Add(imageField.FieldResult);
                        formResults.Status = "FormMatched";
                     }
                     catch (Exception ex)
                     {
                        logger.Error(ex, $"Error on field {field.Name} {rect300}");
                        newForm.Status = $"Error|Field {field.Name} {rect300}: [{ex.Message}]";
                     }
                  }
                  RasterCodecs.Save(PrepareOmrImage(fieldsOnlyImage), fileNameFieldOnly, RasterImageFormat.Png, bitsPerPixel: 8);
                  //Thread.Sleep(1000);
                  //fileNameFieldOnly = @"C:\OCR\99014600682018_02_fields.png";
                  var googleResults = GoogleOcr(fileNameFieldOnly);
                  if (googleResults.Count > 0)
                  {
                     var json = JsonConvert.SerializeObject(googleResults, Formatting.Indented);
                     File.WriteAllText(googleResultsFile, json);

                     MergeGoogleOcr(newForm, googleResults);
                  }

                  usedMasters.Add(currentMasterBlockForm);
               }
               else
               {
                  newForm.Status = "Unmatched|No MasterForm match";
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
         catch (Exception ex)
         {
            logger.Error(ex, "Untrapped error found");
            return null;
         }
      }

     private static void MergeGoogleOcr(FilledForm newForm, List<TextBox> googleResults)
     {
        foreach (var field in newForm.ImageFields.Where(x => x.FieldResult.FieldType == "Text" &&
                                                             !x.Field.Name.Contains("block")))
        {
           if (field.FieldResult?.Text == null)
              continue;
           int bestScore = 10000; // lower is better
           TextBox bestGoogleBox = null;
           var perfectScores = new List<TextBox>();
           foreach (var gr in googleResults)
           {
              var score = field.CalcRectangleMatchScore(gr.Rect);
              if (field.FieldResult.Text.Length > 2)
                 logger.Info(
                    $"{score} [{gr.Description}] {gr.Bounds} {field.Rectangle} [{field.FieldResult.Text}]");
              if (score <= bestScore)
              {
                 if (bestScore == 0)
                 {
                    logger.Info($"second perfect score");
                 }

                 bestScore = score;
                 bestGoogleBox = gr;
                 if (score == 0)
                 {
                    perfectScores.Add(gr);
                 }
              }
           }

           if (bestScore < 30)
           {
              field.FieldResult.GoogleConfidence = bestGoogleBox.Confidence * 100.0;
              field.FieldResult.GoogleText = bestGoogleBox.Description;
              if (perfectScores.Count > 1)
              {
                 field.FieldResult.GoogleConfidence =
                    perfectScores.Min(x => x.Confidence * 100.0);
                 field.FieldResult.GoogleText =
                    perfectScores.Select(x => x.Description).Aggregate("", (a, b) => a + " " + b);
              }
           }
        }
     }

     private FormRecognitionAttributes PrepareNewForm(ImageInfo ofi, Stopwatch stopWatch, FilledForm newForm)
      {
         try
         {
            FormRecognitionAttributes filledFormAttributes = RecognitionEngine.CreateForm(null);
            ofi.Image.ChangeViewPerspective(RasterViewPerspective.TopLeft);
            logger.Info($"{stopWatch.ElapsedMilliseconds} change view");

            for (int i = 0; i < ofi.Image.PageCount; i++)
            {
               ofi.Image.Page = i + 1;
               //Add each page of the filled form to the recognition engine 
               RecognitionEngine.AddFormPage(filledFormAttributes, ofi.Image, null);
            }

            logger.Info($"{stopWatch.ElapsedMilliseconds} closing form");
            RecognitionEngine.CloseForm(filledFormAttributes);
            logger.Info($"{stopWatch.ElapsedMilliseconds} closed form");

            CreateFormForRecognition(newForm, FormsRecognitionMethod.Complex);
            logger.Info($"{stopWatch.ElapsedMilliseconds} create form");
            return filledFormAttributes;
         }
         catch (ThreadAbortException)
         {
            logger.Error($"Thread aborted {stopWatch.ElapsedMilliseconds}");
         }

         return null;
      }

      private void GetOmrReading(IOcrPage ocrPage, FormField field, ImageField imageField, int retry = 1)
      {
         IOcrPageCharacters pageCharacters = ocrPage.GetRecognizedCharacters();
            
         if (pageCharacters == null)
         {
            logger.Warn($"could not read OMR for ${field} ");
            imageField.FieldResult.Confidence = 0;
            imageField.FieldResult.Text = "";
         }
         else
         {
            IOcrZoneCharacters zoneCharacters = pageCharacters[0];
            if (zoneCharacters.Count > 0)
            {
               OcrCharacter omrCharacter = zoneCharacters[0];
               imageField.FieldResult.Text = omrCharacter.Code.ToString();
               imageField.FieldResult.IsFilled = omrCharacter.Code == FilledChar;
               imageField.FieldResult.Confidence = omrCharacter.Confidence;
               // often on a fill we get the line from the box, so we retry more narrowly   
               if (imageField.FieldResult.IsFilled)
               {
                  if (retry > 0)
                  {
                     var orgZone = ocrPage.Zones[0];
                     orgZone.Bounds = ChangeBoundsRatio(orgZone.Bounds, 0.66);
                     ocrPage.Recognize(null);
                     GetOmrReading(ocrPage, field, imageField, 0);
                     logger.Info($"FILLED {field.Name}");
                  }
               }
            }
            else
            {
               imageField.FieldResult.Text = "";
            }
         }
      }

      public static LeadRect ChangeBoundsRatio(LeadRect rect, double ratio)
      {
         int w = (int)(rect.Width * ratio);
         int h = (int)(rect.Height * ratio);
         int x = rect.X + (rect.Width - w) / 2;
         int y = rect.Y + (rect.Height - h) / 2;
         return new LeadRect(x, y, w, h);

      }
   private RasterImage PrepareOmrImage(RasterImage omrImage)
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
         return omrImage;
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
         LineRemoveCommand command = new LineRemoveCommand
         {
            Type = type,
            Flags = LineRemoveCommandFlags.RemoveEntire,
            MaximumLineWidth = 6,
            MinimumLineLength = 30,
            MaximumWallPercent = 10,
            Wall = 10
         };
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

