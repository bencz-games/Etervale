using System.Globalization;
using PristonTale.DataImport;

// ptdata copy <sourceRoot> <assetRoot> [--mask <byte>] <path>...: copies each folder or file of another client's game data
// into the game data, unmasking files whose every byte was XORed with the mask, and refuses what the game cannot read.
if (args.Length < 4 || args[0] != "copy")
{
    Console.Error.WriteLine("usage: ptdata copy <sourceRoot> <assetRoot> [--mask <byte>] <path>...");
    return 2;
}

byte mask = 0;
var paths = new List<string>();
for (var index = 3; index < args.Length; index++)
{
    if (args[index] == "--mask" && index + 1 < args.Length)
    {
        var text = args[++index];
        var parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask)
            : byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask);
        if (!parsed)
        {
            Console.Error.WriteLine($"not a byte: {text}");
            return 2;
        }

        continue;
    }

    paths.Add(args[index]);
}

var result = DataCopier.Copy(args[1], args[2], paths, mask);
foreach (var line in result.Report)
{
    Console.WriteLine(line);
}

Console.WriteLine($"{result.Copied} copied, {result.Kept} already present, {result.Skipped} skipped, {result.Rejected} rejected.");
return result.Rejected == 0 ? 0 : 1;
