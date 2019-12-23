using System;
using System.Collections.Generic;
using Leadtools.Codecs;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Recognition;
using Leadtools.Ocr;
using Leadtools.Forms.Common;
using Leadtools.Forms.Recognition.Ocr;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using Leadtools;
using Google.Cloud.Vision.V1;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using static Google.Cloud.Vision.V1.TextAnnotation.Types.DetectedBreak.Types;

namespace ElevationCertificateSlicer
{
   public class TextBox
   {
      public int Index;
      public string Description;
      public Rectangle Rect;
      public Point LowerRight;
      public long Size;
      public float Confidence;
      public BoundingPoly Bounds;

      public TextBox()
      {

      }
      /*
      public TextBox(BoundingPoly bp, float confidence, string description, int index)
      {
         Description = description;
         Index = index;
         Confidence = confidence;
         Bounds = bp;
         Debug.Assert(bp.Vertices.Count == 4);
         int l = bp.Vertices.Min(x => x.X);
         int r = bp.Vertices.Max(x => x.X);
         int t = bp.Vertices.Min(x => x.Y);
         int b = bp.Vertices.Max(x => x.Y);
         Rect = new Rectangle(new Point(l, t), new Size(r - l, b - t));
         LowerRight = new Point(r, b);
         Size = Rect.Size.Height * Rect.Size.Width;
      }
      */
      public TextBox(Rectangle r1, float confidence, string description, int index)
      {
         Description = description;
         Index = index;
         Confidence = confidence;
         Rect = r1;
         LowerRight = new Point(r1.Right, r1.Bottom);
         Size = Rect.Size.Height * Rect.Size.Width;
      }

      public override string ToString()
      {
         var d = Description;
         d = d.Substring(0, d.Length > 25 ? 25 : d.Length).Replace("\r\n", @"\n");

         return $"({Index}. {Size} {Rect},{Rect.Right},{Rect.Bottom} [{d}] {Description.Length})";
      }
   }

   public class RectFromVertices
   {
      public static Rectangle Convert(BoundingPoly bp)
      {
         int l = bp.Vertices.Min(x => x.X);
         int r = bp.Vertices.Max(x => x.X);
         int t = bp.Vertices.Min(x => x.Y);
         int b = bp.Vertices.Max(x => x.Y);
         var rect = new Rectangle(new Point(l, t), new Size(r - l, b - t));
         return rect;
      }
      public static Rectangle Expand(Rectangle r1, BoundingPoly bp)
      {
         int l = bp.Vertices.Min(x => x.X);
         int r = bp.Vertices.Max(x => x.X);
         int t = bp.Vertices.Min(x => x.Y);
         int b = bp.Vertices.Max(x => x.Y);
         l = (l > r1.Left) ? r1.Left : l;
         r = (r < r1.Right) ? r1.Right : r;
         t = (t > r1.Top) ? r1.Top : t;
         b = (b < r1.Bottom) ? r1.Bottom : b;
         var rect = new Rectangle(new Point(l, t), new Size(r - l, b - t));
         return rect;


      }

   }
   public class Box
   {
      public int Index;
      public EntityAnnotation Annotation;
      public string Description;
      public Rectangle Rect;
      public Point LowerRight;
      public long Size;
      public BoundingPoly Bounds;
      
      public Box Parent;
      public List<Box> Children = new List<Box>();

      public Box()
      {

      }

      public Box(EntityAnnotation ann, int index)
      {
         BoundingPoly bp = ann.BoundingPoly;
         Annotation = ann;
         Description = ann.Description;
         Index = index;
         Bounds = bp;
         Debug.Assert(bp.Vertices.Count == 4);
         int l = bp.Vertices.Min(x => x.X);
         int r = bp.Vertices.Max(x => x.X);
         int t = bp.Vertices.Min(x => x.Y);
         int b = bp.Vertices.Max(x => x.Y);
         Rect = new Rectangle(new Point(l, t), new Size(r - l, b - t));
         LowerRight = new Point(r, b);
         Size = Rect.Size.Height * Rect.Size.Width;
      }

      public bool FindParents(SortedList<long, Box> boxes)
      {
         foreach (var kvp in boxes.Reverse())
         {
            if (kvp.Key < Size)
               break;
            else if (kvp.Value == this)
               continue;
            if (kvp.Value.Contains(this))
            {
               Parent = kvp.Value;
               foreach (var b1 in kvp.Value.Children)
               {
                  if (b1.Contains(this))
                  {
                     b1.Children.Add(this);
                     if (b1.Size < Parent.Size)
                     {
                        Parent = b1;
                     }
                  }
               }

               break;
            }
         }
         return Parent != null;
      }

      public bool Contains(Box b)
      {
         if (b.Size > Size) return false;
         if (b.Rect.X < Rect.X) return false;
         if (b.Rect.Y < Rect.Y) return false;
         if (b.LowerRight.X > LowerRight.X) return false;
         if (b.LowerRight.Y > LowerRight.Y) return false;
         return true;
      }

      public override string ToString()
      {
         var d = Description;
         d = d.Substring(0, d.Length > 25 ? 25 : d.Length).Replace("\r\n", @"\n");

         return $"({Index}. {Size} {Rect},{Rect.Right},{Rect.Bottom} [{d}] {Description.Length})";
      }
   }

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



   class InfoClasses
   {
   }

   public partial class OcrMaster
   {
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
            FilledChar = OcrEngine.ZoneManager.OmrOptions.GetStateRecognitionCharacter(OcrOmrZoneState.Filled);
            UnfilledChar = OcrEngine.ZoneManager.OmrOptions.GetStateRecognitionCharacter(OcrOmrZoneState.Unfilled);

            return true;
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error starting engines");
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
         public double MinConfidence { get; set; }
         public int CertainWords { get; set; }
         public int TotalWords { get; set; }

         public PageResults(double pageConfidence, int certainWords, int totalWords)
         {
            PageConfidence = pageConfidence;
            CertainWords = certainWords;
            TotalWords = totalWords;
            Confidence = 0.25 * PageConfidence + 0.75 * certainWords * 100 / totalWords;
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
         double minConfidence = 101.0;

         for (int i = 0; i < ocrPage.Zones.Count; i++)
         {
            IOcrZoneCharacters zoneCharacters = pageCharacters.FindZoneCharacters(i);

            if (zoneCharacters == null || zoneCharacters.Count == 0)
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

               if (ocrCharacter.Confidence < minConfidence)
                  minConfidence = ocrCharacter.Confidence;
               if (ocrCharacter.Confidence < wordConfidence)
                  wordConfidence = ocrCharacter.Confidence;
               characterCount++;

               if ((ocrCharacter.Position & OcrCharacterPosition.EndOfWord) == OcrCharacterPosition.EndOfWord ||
                   (ocrCharacter.Position & OcrCharacterPosition.EndOfLine) == OcrCharacterPosition.EndOfLine)
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

         PageResults results = new PageResults(pageConfidence, certainWords, totalWords)
         {
            MinConfidence = minConfidence
         };
         if (Double.IsNaN(results.Confidence))
            results.Confidence = minConfidence;
         if (Double.IsNaN(results.Confidence))
            results.Confidence = minConfidence;
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
            form.Resolution = tempProcessingEngine.Pages[0].DpiX;
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

      public class DuplicateKeyComparer<TKey> :
         IComparer<TKey> where TKey : IComparable
      {
         #region IComparer<TKey> Members

         public int Compare(TKey x, TKey y)
         {
            int result = x.CompareTo(y);

            if (result == 0)
               return 1; // Handle equality as being greater
            else
               return result;
         }

         #endregion
      }

      public List<TextBox> GoogleOcr(string file)
      {
         var textBoxes = new List<TextBox>();
         // Instantiates a client
         logger.Info($"Googling {file}");
         try
         {
            var client = ImageAnnotatorClient.Create();
            // Load the image file into memory
            //  var file = @"F:\Dropbox\OCR\Single\data\13864584_3_ocr~20181130_page3pdf.png";
            var responseFile = file + ".google.response.json";
            int num = 0;
            if (!File.Exists(responseFile) || true)
            {
               var image = Google.Cloud.Vision.V1.Image.FromFile(file);

               var response = client.DetectDocumentText(image);
               var serializer = new JsonSerializer { NullValueHandling = NullValueHandling.Ignore };

               using (StreamWriter sw = new StreamWriter(responseFile))
               using (JsonWriter writer = new JsonTextWriter(sw))
               {
                  writer.Formatting = Newtonsoft.Json.Formatting.Indented;
                  serializer.Serialize(writer, response);
               }
            }


            var json2 = File.ReadAllText(responseFile);
            var response2 = JsonConvert.DeserializeObject<TextAnnotation>(json2);
            if (response2?.Pages != null)
            {
               int index = 0;
               float confidence = 1.01f;

               var pageRange = Enumerable.Range(0, response2.Pages.Count);

               foreach (var pnum in pageRange)
               {
                  var page = response2.Pages[pnum];
                  var blockRange = Enumerable.Range(0, page.Blocks.Count);
                  foreach (var bnum in blockRange)
                  {
                     var block = page.Blocks[bnum];
                     logger.Info($"block {bnum} {block.BoundingBox} paras={block.Paragraphs.Count}");
                     var paraRange = Enumerable.Range(0, block.Paragraphs.Count);
                     foreach (var paraNum in paraRange)
                     {
                        var paragraph = block.Paragraphs[paraNum];
                        logger.Info($"{paragraph.BoundingBox} {paragraph.CalculateSize()} {paragraph.Words.Count}");
                        var sb = new StringBuilder();
                        Rectangle rect = Rectangle.Empty;
                        var line = new StringBuilder();

                        foreach (var word in paragraph.Words)
                        {
                           if (word.Confidence < confidence)
                              confidence = word.Confidence;
                           foreach (var symbol in word.Symbols)
                           {
                              line.Append(symbol.Text);
                              if (rect == Rectangle.Empty)
                              {
                                 rect = RectFromVertices.Convert(symbol.BoundingBox);
                              }
                              else rect = RectFromVertices.Expand(rect, symbol.BoundingBox);

                              try
                              {
                                 if (symbol.Property?.DetectedBreak != null)
                                 {
                                    switch (symbol.Property.DetectedBreak.Type)
                                    {
                                       case BreakType.Space:
                                          line.Append(" ");
                                          break;
                                       case BreakType.EolSureSpace:
                                       case BreakType.LineBreak:
                                          index = AddTextBox(index, ref confidence, line, textBoxes, ref rect);
                                          break;
                                       default:
                                          line.Append(" ");
                                          break;
                                    }
                                 }
                              }
                              catch (Exception ex)
                              {
                                 logger.Error(ex, $"{index} of {file} {paragraph.ToString()}");
                              }
                           }
                        }
                        if (line.Length > 0)
                           index = AddTextBox(index, ref confidence, line, textBoxes, ref rect);

                        //logger.Info(b.ToString());
                     }
                  }
               }
               /*
               boxesBySize = new SortedList<long, Box>(new DuplicateKeyComparer<long>());
               var json = File.ReadAllText(boxFile);
               var boxes = JsonConvert.DeserializeObject<List<Box>>(json);
               foreach (var b in boxes)
               {
                  num++;
                  b.Index = num;
                  logger.Info($"{b}");

                  boxesBySize.Add(b.Size, b);
               }

               logger.Info("-----------------------------------------------------");
               foreach (var b in boxes)
               {
                  b.FindParents(boxesBySize);
                  logger.Info(
                     $"{b} cs:{b.Annotation.CalculateSize()} Parent:{b.Parent?.Index}");
               }
               */
            }
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"error processing Google at ${file}");

         }
         return textBoxes;

         //return boxesBySize.Values.ToList<Box>();
      }

      private static int AddTextBox(int index, ref float confidence, StringBuilder line, List<TextBox> textBoxes, ref Rectangle rect)
      {
         index++;
         var tb = new TextBox(rect, confidence, line.ToString().Trim(), index);
         textBoxes.Add(tb);
         rect = Rectangle.Empty;
         confidence = 0.0f;
         line.Clear();
         return index;
      }
      public List<Box> GoogleDetectText(FileInfo fi)
      {
         // Instantiates a client
         var client = ImageAnnotatorClient.Create();
         var file = fi.FullName;
         var detectTextResponse = file + ".google.DetectText.response.json";
         var boxFile = file + ".DetectText.interpreted.json";
         var stem = Path.GetFileNameWithoutExtension(file);

         // Load the image file into memory
         //  var file = @"F:\Dropbox\OCR\Single\data\13864584_3_ocr~20181130_page3pdf.png";
         var allBoxes = new List<Box>();
         var boxesBySize = new SortedList<long, Box>(new DuplicateKeyComparer<long>());
         int num = 0;
         if (!File.Exists(detectTextResponse) || true)
         {
            var image = Google.Cloud.Vision.V1.Image.FromFile(file);


            var serializer = new JsonSerializer { NullValueHandling = NullValueHandling.Ignore };

            var response = client.DetectText(image);
            using (StreamWriter sw = new StreamWriter(detectTextResponse))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
               writer.Formatting = Formatting.Indented;
               serializer.Serialize(writer, response);
               // {"ExpiryDate":new Date(1230375600000),"Price":0}
            }

            foreach (var annotation in response)
            {
               num++;
               if (annotation.Description != null)
               {
                  if (annotation.BoundingPoly == null)
                  {
                     logger.Debug($"{num}. {annotation.ToString()}");
                  }
                  else if (annotation.BoundingPoly.Vertices.Count == 4)
                  {
                     var b = new Box(annotation, allBoxes.Count);
                     allBoxes.Add(b);
                     logger.Debug(
                        $"{num}. {b} cs:{annotation.CalculateSize()} Mid:{annotation.Mid} Parent:{b.Parent?.Index}");
                  }
                  else
                     logger.Debug(
                        $"{num}.Vertices: {annotation.BoundingPoly.Vertices.Count} cs:{annotation.CalculateSize()} {annotation.Description}");
               }
               else
               {
                  logger.Debug($"{num}. {annotation.ToString()}");
               }
            }

            using (StreamWriter sw = new StreamWriter(boxFile))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
               writer.Formatting = Formatting.Indented;
               serializer.Serialize(writer, allBoxes);
               // {"ExpiryDate":new Date(1230375600000),"Price":0}
            }
            var childBoxes = allBoxes.Select(x => x.Children.Count == 0).ToArray();
            logger.Debug($"end boxes {childBoxes.Length}");
         }
         boxesBySize = new SortedList<long, Box>(new DuplicateKeyComparer<long>());
         var json = File.ReadAllText(boxFile);
         var boxes = JsonConvert.DeserializeObject<List<Box>>(json);
         foreach (var b in boxes)
         {
            num++;
            b.Index = num;
            logger.Info($"{b}");

            boxesBySize.Add(b.Size, b);
         }
         logger.Debug("-----------------------------------------------------");
         foreach (var b in boxes)
         {
            b.FindParents(boxesBySize);
            logger.Debug(
               $"{b} cs:{b.Annotation.CalculateSize()} Parent:{b.Parent?.Index}");
         }
         return boxes;
      }
   }
}
