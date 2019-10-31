using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Security.Policy;
using Google.Cloud.Vision.V1;
using Leadtools;

using Leadtools.Codecs;
using Leadtools.Forms.Commands.Internal;
using Leadtools.Forms.Common;
using Leadtools.Forms.Recognition;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Processing.Omr.Fields;
using Leadtools.Ocr;
using Newtonsoft.Json;

namespace ElevationCertificateSlicer
{
   public class ResultsForPrettyJson
   {
      public string PdfFileName;
      public int FieldsWithError;
      public int Resolution = 300;
      public long ElapsedMilliseconds;
      public string OriginalDirectoryName;
      public List<string> TimedOutPages = new List<string>();
      public List<int> BestFormConfidence = new List<int>();
      public int PagesInPdf;
      public string Status = "FormUnmatched";
      public List<string> MasterFormPages = new List<string>();
      public int PagesMappedToForm;
      public List<FieldResultsForPrettyJson> OcrFields = new List<FieldResultsForPrettyJson>();
   }
   
   public class FieldResultsForPrettyJson
   {
      public string FieldName;
      public string FieldType;
      public bool IsBlock;
      public string ImageFile;
      public string Text;
      public double Confidence;
      public string GoogleText;
      public double GoogleConfidence;
      public bool IsFilled;
      public string Bounds;
      public string Error;

   }
   public class ImageField
   {
      public FormField Field;
      public ImageInfo ImageInfo;
      public Rectangle Rectangle;
      public FieldResultsForPrettyJson FieldResult = new FieldResultsForPrettyJson();

      private static void calcScore(int val, ref int score)
      {
         if (val > 0)
         {
            if (val > 5)
               score += 5 + (int) Math.Pow(val - 5, 2);
            else score += val;
         }
      }
      public int CalcRectangleMatchScore(Rectangle rect)
      {
         
         int score = 0;
         calcScore(Rectangle.Left - rect.Left, ref score);
         calcScore(Rectangle.Top - rect.Top, ref score);
         calcScore(rect.Right - Rectangle.Right, ref score);
         calcScore(rect.Bottom - Rectangle.Bottom, ref score);
         return score;
      }

      
   }
   public class FilledForm
   {
      public string Status { get; set; } = "Unmatched";
      public List<ImageField> ImageFields = new List<ImageField>();
      public OcrImageInfo ImageInfoMaster { get; set; } = new OcrImageInfo();
      private string _name;
      private FormRecognitionAttributes _attributes;
      private MasterForm _master;
      private FormRecognitionResult _result;
      private IList<PageAlignment> _alignment;
      private FormPages _processingPages;

      public FilledForm()
      {
         _name = null;
         _attributes = null;
         _master = null;
         _result = null;
         _alignment = null;
      }

      public RasterImage GetImage()
      {
         if (ImageInfoMaster.CenteredImage != null)
            return ImageInfoMaster.CenteredImage.Image;
         return ImageInfoMaster.InitialImage.Image;
      }
      public string Name
      {
         get { return _name; }
         set { _name = value; }
      }

      public IList<PageAlignment> Alignment
      {
         get { return _alignment; }
         set { _alignment = value; }
      }

      public FormRecognitionResult Result
      {
         get { return _result; }
         set { _result = value; }
      }

      public FormRecognitionAttributes Attributes
      {
         get { return _attributes; }
         set { _attributes = value; }
      }

      public MasterForm Master
      {
         get { return _master; }
         set { _master = value; }
      }

      public FormPages ProcessingPages
      {
         get { return _processingPages; }
         set { _processingPages = value; }
      }

      public override string ToString()
      {
         return Name;
      }
   }
}
