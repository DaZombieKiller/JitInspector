using Iced.Intel;
using StringBuilder = System.Text.StringBuilder;

namespace JitInspector
{
    internal sealed class RichStringOutput : FormatterOutput
    {
        private readonly StringBuilder sb;

        public RichStringOutput()
        {
            sb = new StringBuilder();
        }

        public override void Write(string text, FormatterTextKind kind)
        {
            sb.Append("<color=");
            sb.Append(GetColor(kind));
            sb.Append(">");
            sb.Append(text);
            sb.Append("</color>");
        }

        public void Reset()
        {
            sb.Length = 0;
        }

        public string ToStringAndReset()
        {
            string result = ToString();
            Reset();
            return result;
        }

        public override string ToString()
        {
            return sb.ToString();
        }

        private static string GetColor(FormatterTextKind kind)
        {
            switch (kind)
            {
            case FormatterTextKind.Directive:
            case FormatterTextKind.Keyword:
                return "#CCCCCC";
            case FormatterTextKind.Prefix:
            case FormatterTextKind.Mnemonic:
                return "#4EC9B0";
            case FormatterTextKind.Register:
                return "#D7BA7D";
            case FormatterTextKind.FunctionAddress:
            case FormatterTextKind.LabelAddress:
            case FormatterTextKind.Number:
                return "#9CDCFE";
            default:
                return "white";
            }
        }
    }
}
