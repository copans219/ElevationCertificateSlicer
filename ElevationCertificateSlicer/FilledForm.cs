using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.IO;
using System.Security.Policy;
using Leadtools;

using Leadtools.Codecs;
using Leadtools.Forms.Commands.Internal;
using Leadtools.Forms.Common;
using Leadtools.Forms.Recognition;
using Leadtools.Forms.Processing;
using Leadtools.Forms.Processing.Omr.Fields;

namespace ElevationCertificateSlicer
{
   public class ImageField
   {
      public FormField Field;
      public ImageInfo ImageInfo;
      public string Text;
      public double Confidence;
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
