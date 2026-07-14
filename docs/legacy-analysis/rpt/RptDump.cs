using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;

// Dumps the definition of every .rpt in a folder to JSON:
// sections, objects (with geometry + text), parameters, formulas, datasource/connection.
// Read-only: reports are opened, never saved.
class RptDump
{
    static readonly string[] ObjProps =
    {
        "Name", "Kind", "Left", "Top", "Width", "Height", "ObjectFormat"
    };

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        string outPath = args.Length > 1 ? args[1] : "rpt-dump.json";

        var reports = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.rpt").OrderBy(x => x))
        {
            Console.Error.WriteLine("reading " + Path.GetFileName(f));
            try { reports.Add(DumpOne(f)); }
            catch (Exception ex)
            {
                reports.Add("{\"file\":" + Q(Path.GetFileName(f)) + ",\"error\":" + Q(ex.Message) + "}");
            }
        }

        File.WriteAllText(outPath, "[\n" + string.Join(",\n", reports) + "\n]", Encoding.UTF8);
        Console.Error.WriteLine("wrote " + outPath);
    }

    static string DumpOne(string path)
    {
        var rd = new ReportDocument();
        rd.Load(path, OpenReportMethod.OpenReportByTempCopy);

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"file\":").Append(Q(Path.GetFileName(path)));

        // ---- connection / datasource (checking for embedded credentials) ----
        sb.Append(",\"tables\":[");
        var tparts = new List<string>();
        foreach (Table t in rd.Database.Tables)
        {
            var ci = t.LogOnInfo.ConnectionInfo;
            var t2 = new StringBuilder("{");
            t2.Append("\"name\":").Append(Q(t.Name));
            t2.Append(",\"location\":").Append(Q(t.Location));
            t2.Append(",\"server\":").Append(Q(ci.ServerName));
            t2.Append(",\"database\":").Append(Q(ci.DatabaseName));
            t2.Append(",\"user\":").Append(Q(ci.UserID));
            t2.Append(",\"passwordPresent\":").Append((!string.IsNullOrEmpty(ci.Password)).ToString().ToLower());
            var cols = new List<string>();
            foreach (DatabaseFieldDefinition fd in t.Fields)
                cols.Add("{\"name\":" + Q(fd.Name) + ",\"type\":" + Q(fd.ValueType.ToString()) + "}");
            t2.Append(",\"fields\":[").Append(string.Join(",", cols)).Append("]}");
            tparts.Add(t2.ToString());
        }
        sb.Append(string.Join(",", tparts)).Append("]");

        // ---- parameters: this is the template's public contract ----
        sb.Append(",\"parameters\":[");
        var pparts = new List<string>();
        foreach (ParameterFieldDefinition p in rd.DataDefinition.ParameterFields)
        {
            var p2 = new StringBuilder("{");
            p2.Append("\"name\":").Append(Q(p.Name));
            p2.Append(",\"type\":").Append(Q(p.ValueType.ToString()));
            p2.Append(",\"prompt\":").Append(Q(Safe(() => p.PromptText)));
            p2.Append("}");
            pparts.Add(p2.ToString());
        }
        sb.Append(string.Join(",", pparts)).Append("]");

        // ---- formulas ----
        sb.Append(",\"formulas\":[");
        var fparts = new List<string>();
        foreach (FormulaFieldDefinition ff in rd.DataDefinition.FormulaFields)
            fparts.Add("{\"name\":" + Q(ff.Name) + ",\"text\":" + Q(Safe(() => ff.Text)) + "}");
        sb.Append(string.Join(",", fparts)).Append("]");

        // ---- sections + objects (geometry in twips: 1440 = 1 inch) ----
        sb.Append(",\"sections\":[");
        var sparts = new List<string>();
        foreach (Section sec in rd.ReportDefinition.Sections)
        {
            var s2 = new StringBuilder("{");
            s2.Append("\"name\":").Append(Q(sec.Name));
            s2.Append(",\"height\":").Append(sec.Height.ToString(CultureInfo.InvariantCulture));
            s2.Append(",\"objects\":[");
            var oparts = new List<string>();
            foreach (ReportObject ro in sec.ReportObjects)
                oparts.Add(DumpObject(ro));
            s2.Append(string.Join(",", oparts)).Append("]}");
            sparts.Add(s2.ToString());
        }
        sb.Append(string.Join(",", sparts)).Append("]");

        sb.Append("}");
        rd.Close();
        return sb.ToString();
    }

    static string DumpObject(ReportObject ro)
    {
        var sb = new StringBuilder("{");
        var bits = new List<string>();

        foreach (var pn in ObjProps)
        {
            PropertyInfo pi = ro.GetType().GetProperty(pn);
            if (pi == null) continue;
            object v = Safe<object>(() => pi.GetValue(ro, null));
            if (v == null) continue;
            if (pn == "ObjectFormat") continue; // nested; skip
            bits.Add(Q(ToCamel(pn)) + ":" + Lit(v));
        }

        // type-specific payloads
        var to = ro as TextObject;
        if (to != null)
        {
            bits.Add("\"text\":" + Q(Safe(() => to.Text)));
            bits.Add("\"font\":" + Q(Safe(() => to.Font.Name + " " + to.Font.SizeInPoints + (to.Font.Bold ? " bold" : ""))));
            bits.Add("\"color\":" + Q(Safe(() => to.Color.Name)));
        }

        var fo = ro as FieldObject;
        if (fo != null)
        {
            bits.Add("\"dataSource\":" + Q(Safe(() => fo.DataSource != null ? fo.DataSource.FormulaName : "")));
            bits.Add("\"font\":" + Q(Safe(() => fo.Font.Name + " " + fo.Font.SizeInPoints + (fo.Font.Bold ? " bold" : ""))));
            bits.Add("\"color\":" + Q(Safe(() => fo.Color.Name)));
        }

        var so = ro as SubreportObject;
        if (so != null)
            bits.Add("\"subreport\":" + Q(Safe(() => so.SubreportName)));

        sb.Append(string.Join(",", bits)).Append("}");
        return sb.ToString();
    }

    static string ToCamel(string s) { return char.ToLowerInvariant(s[0]) + s.Substring(1); }

    static string Lit(object v)
    {
        if (v is bool) return ((bool)v) ? "true" : "false";
        if (v is int || v is long || v is short || v is double || v is float || v is decimal)
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        return Q(v.ToString());
    }

    static T Safe<T>(Func<T> f) { try { return f(); } catch { return default(T); } }
    static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

    static string Q(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append("\"").ToString();
    }
}
