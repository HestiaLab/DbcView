using System.Globalization;
using System.Text;

namespace DbcView.Web.Services;

/// <summary>Deterministic 4 MB DBC envelope for the development performance probe.</summary>
public static class DbcSyntheticGenerator
{
    private const string PaddingLine =
        "CM_ \"padding comment for four megabyte envelope ................................................................\";";

    public static string Create(
        int messageCount = 28000,
        int signalsPerMessage = 2,
        int semanticCount = 10000,
        int targetBytes = 4_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(messageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(signalsPerMessage, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(semanticCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetBytes, 0);

        var builder = new StringBuilder(Math.Max(targetBytes, messageCount * 160));
        builder.AppendLine("VERSION \"\"");
        builder.AppendLine("BU_: ECU1 ECU2");
        builder.AppendLine("BA_DEF_ BO_ \"GenMsgCycleTime\" INT 0 1000;");
        builder.AppendLine();

        var messageLength = Math.Max(1, signalsPerMessage);
        for (var index = 0; index < messageCount; index++)
        {
            var id = 0x100 + index;
            builder.Append("BO_ ")
                .Append(id.ToString(CultureInfo.InvariantCulture))
                .Append(" Msg_")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(messageLength.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" ECU1");

            for (var signal = 0; signal < signalsPerMessage; signal++)
            {
                builder.Append(" SG_ Sig_")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append('_')
                    .Append(signal.ToString(CultureInfo.InvariantCulture))
                    .Append(" : ")
                    .Append((signal * 8).ToString(CultureInfo.InvariantCulture))
                    .AppendLine("|8@0+ (1,0) [0|255] \"\" ECU2");
            }
        }

        for (var index = 0; index < semanticCount; index++)
        {
            var messageIndex = index % messageCount;
            var id = (0x100 + messageIndex).ToString(CultureInfo.InvariantCulture);
            var signalName = "Sig_" + messageIndex.ToString(CultureInfo.InvariantCulture) + "_0";
            switch (index % 10)
            {
                case < 5 when signalsPerMessage > 0:
                    builder.Append("CM_ SG_ ")
                        .Append(id)
                        .Append(' ')
                        .Append(signalName)
                        .Append(" \"Generated comment ")
                        .Append(index.ToString(CultureInfo.InvariantCulture))
                        .AppendLine("\";");
                    break;
                case < 8:
                    builder.Append("BA_ \"GenMsgCycleTime\" BO_ ")
                        .Append(id)
                        .Append(' ')
                        .Append((10 + (index % 100)).ToString(CultureInfo.InvariantCulture))
                        .AppendLine(";");
                    break;
                case >= 8 when signalsPerMessage > 0:
                    builder.Append("VAL_ ")
                        .Append(id)
                        .Append(' ')
                        .Append(signalName)
                        .AppendLine(" 0 \"Off\" 1 \"On\";");
                    break;
            }
        }

        if (targetBytes > 0)
        {
            while (builder.Length < targetBytes)
            {
                builder.AppendLine(PaddingLine);
            }
        }

        return builder.ToString();
    }
}
