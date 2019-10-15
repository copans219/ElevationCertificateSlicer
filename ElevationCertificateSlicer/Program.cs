using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Leadtools;
using Leadtools.Codecs;
using Leadtools.Forms.Recognition.Ocr;
using Leadtools.Ocr;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Recognition;
using Leadtools.Forms.Common;
using Leadtools.Forms.Recognition.Barcode;
using Leadtools.ImageProcessing.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Permissions;
using Newtonsoft.Json.Linq;

namespace ElevationCertificateSlicer
{
   class Program
   {
      public const string
         CertificateDirString = @"F:\Dropbox\Danny\Flood\OCR\Elevation Certificates New"; //@"C:\Users\copan\Downloads\Elevation Certificates New";

      public const string CertificatePdfSample =
         @"F:\Dropbox\Danny\Flood\OCR\Elevation Certificates New\13984799.pdf";

      public enum OutputFormat
      {
         PDF,
         PDF_EMBED,
         PDFA,
         PDF_IMAGE_OVER_TEXT,
         PDF_EMBED_IMAGE_OVER_TEXT,
         PDFA_IMAGE_OVER_TEXT,
         DOCX,
         DOCX_FRAMED,
         RTF,
         RTF_FRAMED,
         TEXT,
         TEXT_FORMATTED,
         SVG,
         ALTO_XML,
         HTM,
         EPUB,
         MOBI
      }

      private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

      // <summary>
      // LEADOcr
      // </summary>
      // <param name="path">Either a single PDF or a director</param>
      // <param name="endLevel">Levels are 1: extract image. 2: detect form, 3:</param>
      static void Main(string path = CertificatePdfSample, int endLevel = 3)
      {
         var stopWatch = new Stopwatch();
         stopWatch.Start();
         logger.Info($"path={path}, endLevel={endLevel}");
         try
         {
            string licString =
               @"[License]\nLicense = <doc><ver>2.0</ver><code>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</code></doc>";

            byte[] licBytes = System.Text.Encoding.UTF8.GetBytes(licString);
            string key =
               @"i8xgXvVTrpbjbRHDPdFZk9+RWcBrLjUIlt233v5p4TOpoJPYBGOG1xqYtXqhCnFE"; //"PASTE YOUR DEVELOPER KEY HERE";
            RasterSupport.SetLicense(licBytes, key);
         }
         catch (Exception ex)
         {
            logger.Error(ex.Message);
         }

         if (RasterSupport.KernelExpired)
         {
            throw new Exception("Invalid license");
         }
         //TestOcr2();
         var dataDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
         logger.Info($"dir = {dataDirectory}");
         var filesToDo = new List<string>();
         if (path != null)
         {
            if (File.Exists(path))
            {
               filesToDo.Add(path);
            }
            else if (Directory.Exists(path))
            {
               foreach (var file in Directory.GetFiles(path))
               {
                  var fi = new FileInfo(file);
                  if (fi.Extension == ".pdf" || fi.Extension == ".tif")
                     filesToDo.Add(file);
               }
            }
            else
            {
               logger.Error($"invalid pdf/dir 'path' parameter: {path}");
               return;
            }
         }

         if (filesToDo.Count > 0)
         {
            var ocrMaster = new OcrMaster();
            foreach (var pdfFile in filesToDo)
            {
               try
               {
                  if(filesToDo.Count > 1)
                     stopWatch.Restart(); 
                  var fi = new FileInfo(pdfFile);
                  var stem = Path.GetFileNameWithoutExtension(pdfFile);
                  var dirTiff = Path.Combine(fi.DirectoryName, stem);
                  Directory.CreateDirectory(dirTiff);

                  var outFileTemplate = Path.Combine(dirTiff, stem + "_{page}.png");
                  logger.Info(outFileTemplate);
                  var pngFiles = ConvertDocumentToImage(pdfFile, outFileTemplate, RasterImageFormat.Png, 8, null);
                  var formResults = new ResultsForPrettyJson()
                  {
                     PdfFileName = fi.Name,
                     OriginalDirectoryName = dirTiff,
                  };
                  ocrMaster.ProcessOcr(formResults, pngFiles);
                  var baseName = Path.GetFileNameWithoutExtension(formResults.PdfFileName);
                  var jsonName = Path.Combine(dirTiff, baseName + ".json");
                  formResults.ElsapsedMilliseconds = stopWatch.ElapsedMilliseconds;
                  var json = JsonConvert.SerializeObject(formResults, Formatting.Indented);
                  File.WriteAllText(jsonName, json);
                  logger.Info($"Writing to {jsonName}, {stopWatch.ElapsedMilliseconds} milliseconds");

               }
               catch (Exception e)
               {
                  logger.Error($"File {pdfFile} error {e}");
               }
            }
         }
         logger.Info($"Completed in {stopWatch.ElapsedMilliseconds} milliseconds");
      }
      public static void TestOcr()
      {
         try
         {
            //string[] masterFormAttributes = Directory.GetFiles(BaseFolder, "2*.bin", SearchOption.AllDirectories);
            //Get master form filenames 
            //You may need to update the below path to point to the "Leadtools Images\Forms\MasterForm Sets\OCR" directory. 
            string[] masterFileNames = Directory.GetFiles(
               @"C:\Users\Public\Documents\LEADTOOLS Images\Forms\MasterForm Sets\OCR",
               "*.tif",
               SearchOption.AllDirectories);

            FormRecognitionEngine recognitionEngine = new FormRecognitionEngine();
            RasterCodecs codecs;
            //Create the OCR Engine to use in the recognition 
            IOcrEngine formsOCREngine;
            codecs = new RasterCodecs();
            //Create a LEADTOOLS OCR Module - LEAD Engine and start it 
            formsOCREngine = OcrEngineManager.CreateEngine(OcrEngineType.OmniPage, false);
            //formsOCREngine.Startup(codecs, null, null, @"C:\LEADTOOLS 20\Bin\Common\OcrLEADRuntime");
            formsOCREngine.Startup(codecs, null, null, null);
            //Add an OCRObjectManager to the recognition engines 
            //ObjectManager collection 
            OcrObjectsManager ocrObjectsManager = new OcrObjectsManager(formsOCREngine);
            ocrObjectsManager.Engine = formsOCREngine;
            recognitionEngine.ObjectsManagers.Add(ocrObjectsManager);
            var binFiles = new List<string>();

            foreach (string masterFileName in masterFileNames)
            {
               string formName = Path.GetFileNameWithoutExtension(masterFileName);
               //Load the master form image 
               RasterImage image = codecs.Load(masterFileName, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
               //Create a new master form 
               FormRecognitionAttributes masterFormAttributes =
                  recognitionEngine.CreateMasterForm(formName, Guid.Empty, null);
               for (int i = 0; i < image.PageCount; i++)
               {
                  image.Page = i + 1;
                  //Add the master form page to the recognition engine 
                  recognitionEngine.AddMasterFormPage(masterFormAttributes, image, null);
               }

               //Close the master form and save it's attributes 
               recognitionEngine.CloseMasterForm(masterFormAttributes);
               var binFile = formName + ".bin";
               File.WriteAllBytes(binFile, masterFormAttributes.GetData());
               binFiles.Add(binFile);
            }

            logger.Info("Master Form Processing Complete {0}", "Complete");
            //For this tutorial, we will use the sample W9 filled form. 
            //You may need to update the below path to point to "\LEADTOOLS Images\Forms\Forms to be Recognized\OCR\W9_OCR_Filled.tif". 
            if (true)
            {
               string formToRecognize =
                  @"C:\Users\Public\Documents\LEADTOOLS Images\Forms\Forms to be Recognized\OCR\W9_OCR_Filled.tif";
               RasterImage image = codecs.Load(formToRecognize, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
               //Load the image to recognize 
               FormRecognitionAttributes filledFormAttributes = recognitionEngine.CreateForm(null);
               for (int i = 0; i < image.PageCount; i++)
               {
                  image.Page = i + 1;
                  //Add each page of the filled form to the recognition engine 
                  recognitionEngine.AddFormPage(filledFormAttributes, image, null);
               }

               recognitionEngine.CloseForm(filledFormAttributes);
               string resultMessage = "The form could not be recognized";
               //Compare the attributes of each master form to the attributes of the filled form 
               foreach (string masterFileName in binFiles)
               {
                  FormRecognitionAttributes masterFormAttributes = new FormRecognitionAttributes();
                  masterFormAttributes.SetData(File.ReadAllBytes(masterFileName));
                  FormRecognitionResult recognitionResult =
                     recognitionEngine.CompareForm(masterFormAttributes, filledFormAttributes, null);
                  //In this example, we consider a confidence equal to or greater 
                  //than 90 to be a match 
                  if (recognitionResult.Confidence >= 90)
                  {
                     resultMessage = String.Format("This form has been recognized as a {0}",
                        Path.GetFileNameWithoutExtension(masterFileName));
                     break;
                  }
               }

               logger.Info(resultMessage, "Recognition Results");
            }

         }
         catch (Exception ex)
         {
            logger.Info(ex.Message);
            throw;
         }
      }
      public static void TestOcr2()
      {
         string BaseFolder =
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

         try
         {
            string[] masterFileNames = Directory.GetFiles(BaseFolder, "2*.tif", SearchOption.AllDirectories);
            //Get master form filenames 
            //You may need to update the below path to point to the "Leadtools Images\Forms\MasterForm Sets\OCR" directory. 

            FormRecognitionEngine recognitionEngine = new FormRecognitionEngine();
            RasterCodecs codecs;
            //Create the OCR Engine to use in the recognition 
            IOcrEngine formsOCREngine;
            codecs = new RasterCodecs();
            //Create a LEADTOOLS OCR Module - LEAD Engine and start it 
            formsOCREngine = OcrEngineManager.CreateEngine(OcrEngineType.OmniPage, false);
            //formsOCREngine.Startup(codecs, null, null, @"C:\LEADTOOLS 20\Bin\Common\OcrLEADRuntime");
            formsOCREngine.Startup(codecs, null, null, null);
            //Add an OCRObjectManager to the recognition engines 
            //ObjectManager collection 
            OcrObjectsManager ocrObjectsManager = new OcrObjectsManager(formsOCREngine);
            ocrObjectsManager.Engine = formsOCREngine;
            recognitionEngine.ObjectsManagers.Add(ocrObjectsManager);
            var binFiles = new List<string>();

            foreach (string masterFileName in masterFileNames)
            {
               string formName = Path.GetFileNameWithoutExtension(masterFileName);
               //Load the master form image 
               RasterImage image = codecs.Load(masterFileName, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
               //Create a new master form 
               FormRecognitionAttributes masterFormAttributes =
                  recognitionEngine.CreateMasterForm(formName, Guid.Empty, null);
               for (int i = 0; i < image.PageCount; i++)
               {
                  image.Page = i + 1;
                  //Add the master form page to the recognition engine 
                  recognitionEngine.AddMasterFormPage(masterFormAttributes, image, null);
               }

               //Close the master form and save it's attributes 
               recognitionEngine.CloseMasterForm(masterFormAttributes);
               var binFile = formName + "_runtime.bin";
               File.WriteAllBytes(binFile, masterFormAttributes.GetData());
               binFiles.Add(binFile);
            }

            logger.Info("Master Form Processing Complete {0}", "Complete");
            //For this tutorial, we will use the sample W9 filled form. 
            //You may need to update the below path to point to "\LEADTOOLS Images\Forms\Forms to be Recognized\OCR\W9_OCR_Filled.tif". 
            if (true)
            {
               string formToRecognize = Path.Combine(BaseFolder, "13984799_02.png");
               RasterImage image = codecs.Load(formToRecognize, 0, CodecsLoadByteOrder.BgrOrGray, 1, -1);
               //Load the image to recognize 
               FormRecognitionAttributes filledFormAttributes = recognitionEngine.CreateForm(null);
               for (int i = 0; i < image.PageCount; i++)
               {
                  image.Page = i + 1;
                  //Add each page of the filled form to the recognition engine 
                  recognitionEngine.AddFormPage(filledFormAttributes, image, null);
               }

               recognitionEngine.CloseForm(filledFormAttributes);
               string resultMessage = "The form could not be recognized";
               //Compare the attributes of each master form to the attributes of the filled form 
               foreach (string masterFileName in binFiles)
               {
                  FormRecognitionAttributes masterFormAttributes = new FormRecognitionAttributes();
                  masterFormAttributes.SetData(File.ReadAllBytes(masterFileName));
                  FormRecognitionResult recognitionResult =
                     recognitionEngine.CompareForm(masterFormAttributes, filledFormAttributes, null);
                  //In this example, we consider a confidence equal to or greater 
                  //than 90 to be a match 
                  if (recognitionResult.Confidence >= 90)
                  {
                     resultMessage = String.Format("This form has been recognized as a {0}",
                        Path.GetFileNameWithoutExtension(masterFileName));
                     break;
                  }
               }

               logger.Info(resultMessage, "Recognition Results");
            }

         }
         catch (Exception ex)
         {
            logger.Info(ex.Message);
            throw;
         }
      }


      public static List<ImageInfo> ConvertDocumentToImage(
          string inputFile,
          string outputFileTemplate,
          RasterImageFormat outputFormat,
          int bitsPerPixel,
          HashSet<string> justThese)
      {
         if (justThese == null)
            justThese = new HashSet<string>();
         if (!File.Exists(inputFile))
            throw new ArgumentException($"{inputFile} not found.", nameof(inputFile));

         if (bitsPerPixel != 0 && bitsPerPixel != 1 && bitsPerPixel != 2 && bitsPerPixel != 4 &&
             bitsPerPixel != 8 && bitsPerPixel != 16 && bitsPerPixel != 24 && bitsPerPixel != 32)
            throw new ArgumentOutOfRangeException(nameof(bitsPerPixel), bitsPerPixel,
               $"Invalid {nameof(bitsPerPixel)} value");

         var retFiles = new List<ImageInfo>();
         using (var codecs = new RasterCodecs())
         {
            codecs.Options.RasterizeDocument.Load.XResolution = 300;
            codecs.Options.RasterizeDocument.Load.YResolution = 300;

            // indicates the start of a loop from the same source file
            codecs.StartOptimizedLoad();

            var totalPages = codecs.GetTotalPages(inputFile);
            /*
            if (totalPages > 1 && !RasterCodecs.FormatSupportsMultipageSave(outputFormat))
               throw new NotSupportedException(
                  $"The {outputFormat} format does not support multiple pages.");
                  */
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
               string newOutfile = outputFileTemplate.Replace("{page}", pageNumber.ToString("D2"));
               string stem = Path.GetFileNameWithoutExtension(newOutfile);
               if (justThese.Count > 0 && !justThese.Contains(stem))
                  continue;
               if (File.Exists(newOutfile))
               {
                  logger.Info($"File already exists {newOutfile}");
                  retFiles.Add(new ImageInfo() { ImageFileInfo = new FileInfo(newOutfile) });
                  continue;
               }

               logger.Info($"Loading and saving page {newOutfile}");
               var rasterImage =
                  codecs.Load(inputFile, bitsPerPixel, CodecsLoadByteOrder.Bgr, pageNumber, pageNumber);
               codecs.Save(rasterImage, newOutfile, outputFormat, bitsPerPixel, 1, -1, 1, CodecsSavePageMode.Replace);
               retFiles.Add(new ImageInfo() { Image = rasterImage, ImageFileInfo = new FileInfo(newOutfile) });
            }

            // indicates the end of the load for the source file
            codecs.StopOptimizedLoad();
         }
         return retFiles;
      }
   }
}
