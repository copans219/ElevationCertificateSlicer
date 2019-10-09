using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ElevationCertificateSlicer
{

   public class OcrField
   {
      public string VariableName;
      public string VariableType;
      public string Text;
      public int MaximumConfidence;
      public int MinimumConfidence;
      public int AverageConfidence;
      public string Bounds;
      public string Summary;

      public OcrField(string variableName, string variableType, string bounds, JToken res)
      {
         VariableName = variableName;
         VariableType = variableType;
         Bounds = bounds.TrimEnd('\r', '\n'); ;
         Text = res.Value<string>("Text")?.TrimEnd('\r', '\n') ?? ""; ;
         MinimumConfidence = res.Value<int?>("MinimumConfidence") ?? -1;
         MaximumConfidence = res.Value<int?>("MaximumConfidence") ?? -1;
         AverageConfidence = res.Value<int?>("AverageConfidence") ?? -1;
         Summary = $"[{VariableName}]=[{Text.Replace("\r", "\\r").Replace("\n", "\\n")}]";
      }
   }

   public class OcrRecord
   {
      public string AppID;
      public List<string> Files = new List<string>();
      public List<string> UnrecognizedFiles = new List<string>();
      public List<string> MasterForms = new List<string>();
      public Dictionary<string, OcrField> Fields = new Dictionary<string, OcrField>();

      public static OcrRecord GetOrCreate(string appId, Dictionary<string, OcrRecord> keeper)
      {
         if (keeper.ContainsKey(appId))
         {
            var prev = keeper[appId];
            return prev;
         }

         var n = new OcrRecord()
         {
            AppID = appId
         };
         keeper[n.AppID] = n;
         return n;

      }

      public void AddField(OcrField f)
      {
         Fields[f.VariableName] = f;
      }

      public static Tuple<string, string, int> FindAppID(string recognized)
      {
         var p = recognized.Split(new string[] { @"Page1\" }, StringSplitOptions.None)[1]?.Split('.')[0]?.Split('_');
         if (p == null || p.Length != 2)
            return new Tuple<string, string, int>("not found", "not found", -1);
         var file = string.Join("_", p);
         var id = p[0];
         if (Int32.TryParse(p[1], out var page))
         {
            return new Tuple<string, string, int>(id, file, page);
         }
         return new Tuple<string, string, int>("bad page", file, -1);
      }
   }
}