using System.IO;

namespace JitInspector
{
    internal static class TextWriterExtensions
    {
        public static void BeginComment(this TextWriter writer, bool rich)
        {
            if (rich)
            {
                writer.Write("<color=#6A9955>");
            }

            writer.Write(";\u00A0");
        }

        public static void EndComment(this TextWriter writer, bool rich)
        {
            if (rich)
            {
                writer.WriteLine("</color>");
            }
            else
            {
                writer.WriteLine();
            }
        }

        public static void WriteComment(this TextWriter writer, string comment, bool rich)
        {
            BeginComment(writer, rich);
            writer.Write(comment);
            EndComment(writer, rich);
        }

        public static void WriteColored(this TextWriter writer, string text, string color)
        {
            writer.Write("<color=");
            writer.Write(color);
            writer.Write('>');
            writer.Write(text);
            writer.Write("</color>");
        }
    }
}
