using PristonTale.Assets;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Formats.Gltf;

namespace PristonTale.Engine3D.Formats;

// A model descriptor in text (.ini, .inf; CP949), read into the PristonModelInfo its model-info file (.inx) holds, as
// PristonInxWriter compiles it. Each line starts with a command, its words split by spaces and tabs (a word opening
// with a quote runs to the closing one), at most 511 bytes a line:
// - *모양파일 the model, *동작파일 the motion file, *보조동작파일 the sub-model, *파일연결 the model whose motions it borrows,
//   *표정파일 and *표정파일연결 the talk motions' file and the model they are borrowed from; each a name beside the
//   descriptor, stored with the descriptor's directory (char\...\).
// - *정밀모양, *보통모양, *저질모양: up to four object names of each level of detail.
// - *동작모음 (and *표정모음) adds a motion file: the motions after it play from it (MotionFrame, 1-based). A glTF model may
//   follow with a clip's name, the motions after it playing that clip (its index + 1); without one, the n-th collection
//   plays the n-th clip.
// - Any other command naming 동작 is a motion from slot CharacterMotionBase: start and end frame, up to four event frames
//   (stored in ticks from the start), then 반복 (loops) or a key character, then a chance (n) of the NPC's idle choice.
//   Its state comes from the words of its name (WordStates, the last match winning); a stand, walk or run loops; a death
//   ends 8 frames early. *착용무기, *적용직업, *해당위치 and *적용기술 restrict the motion before them to weapons (모두 any,
//   없음 bare hands), classes, the village (마을) or the field (필드), and skills.
// - A command naming 표정 after a motion file is a talk motion of the last file (MotionFrame 0-based), its state from its
//   name (TalkStates, the first match).
// The idle and talk chances become the 100-slot selection tables, the largest chance taking up the rest of 100.
public static class PristonModelText
{
    private const int LineCapacity = 511;
    private const uint Tick = 160;
    private const int DeadEndFrames = 8;
    private const int RateCapacity = 100;
    private const int TalkFile = 0;
    private const int FacialFile = 1;

    // Read switches on the index in this list.
    private static readonly string[] Commands =
    [
        "*동작파일", "*동작모음", "*모양파일", "*모양이름", "*파일연결", "*보조동작파일", "*정밀모양", "*보통모양", "*저질모양",
        "*XX걷는동작", "*XX서있기동작", "*착용무기", "*적용직업", "*해당위치", "*적용기술", "*표정파일연결", "*표정파일", "*표정모음"
    ];

    private static readonly (string Word, MotionCode State, bool Loops)[] WordStates =
    [
        ("뛰는", MotionCode.Run, true), ("공격", MotionCode.Attack, false), ("죽기", MotionCode.Dead, false), ("죽는", MotionCode.Dead, false),
        ("타격", MotionCode.Damage, false), ("데미지", MotionCode.Damage, false), ("막기", MotionCode.Damage, false),
        ("가끔", MotionCode.Sometime, false), ("떨어", MotionCode.FallDown, false), ("작은착지", MotionCode.FallStand, false),
        ("큰착지", MotionCode.FallDamage, false), ("*서있기", MotionCode.Stand, true), ("*정지", MotionCode.Stand, true),
        ("*걷는", MotionCode.Walk, true), ("다시시작", MotionCode.Restart, false), ("먹기", MotionCode.Eat, false),
        ("기술", MotionCode.Skill, false), ("울기", MotionCode.Warp, false), ("워프", MotionCode.Warp, false), ("만세", MotionCode.Yahoo, false)
    ];

    private static readonly (string Word, MotionCode State)[] TalkStates =
    [
        ("아표정", MotionCode.TalkAh), ("이표정", MotionCode.TalkEe), ("오표정", MotionCode.TalkOh), ("눈깜빡표정", MotionCode.TalkEyeBlink),
        ("무표정(작동않함)", MotionCode.Any), ("웃는표정", MotionCode.TalkSmile), ("화난표정", MotionCode.TalkAnger),
        ("슬픈표정", MotionCode.TalkSorrow), ("놀란표정", MotionCode.TalkStartled), ("고유표정", MotionCode.TalkNeutral),
        ("특별표정", MotionCode.TalkSpecial)
    ];

    private static readonly byte[][] CommandBytes = [.. Commands.Select(static command => Cp949.Encoding.GetBytes(command))];
    private static readonly byte[] MotionWord = Cp949.Encoding.GetBytes("동작");
    private static readonly byte[] TalkWord = Cp949.Encoding.GetBytes("표정");

    public static bool IsDescriptor(string path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) || extension.Equals(".inf", StringComparison.OrdinalIgnoreCase));

    // path: the descriptor on disk, under the assets root (its place there names the stored files' directory).
    public static PristonModelInfo Read(string path)
    {
        var data = Plain(AssetFiles.ReadAllBytes(path));
        var canonical = PristonInxReader.ToCanonicalAssetPath(path);
        var directory = canonical[..(canonical.LastIndexOf('\\') + 1)];
        var model = new Builder();
        var motionFiles = new List<string>();
        var clipFrame = 0;
        var last = 0;
        foreach (var line in Lines(data))
        {
            var cursor = new Words(line);
            var command = cursor.Next();
            var index = Array.FindIndex(CommandBytes, bytes => command.SequenceEqual(bytes));
            if (index < 0)
            {
                if (command.Length > 0 && command[0] == '*' && command.AsSpan().IndexOf(MotionWord) >= 0)
                {
                    if (model.MotionCount >= PristonFormatConstants.MotionInfoCount)
                    {
                        throw new InvalidDataException($"{path} has more motions than a model-info file holds ({PristonFormatConstants.MotionInfoCount}).");
                    }

                    var slot = (int)model.MotionCount;
                    var motion = model.Motions[slot];
                    ReadMotion(motion, ref cursor, model.Rates, slot);
                    motion.MotionFrame = clipFrame > 0 ? clipFrame : motionFiles.Count;
                    motion.State = 1;
                    var name = Cp949.Encoding.GetString(command);
                    foreach (var (word, state, loops) in WordStates)
                    {
                        if (name.Contains(word, StringComparison.Ordinal))
                        {
                            motion.State = (uint)state;
                            motion.Repeat = loops ? 1u : motion.Repeat;
                        }
                    }

                    if (motion.State == (uint)MotionCode.Skill)
                    {
                        motion.SkillCodes[0] = 0;
                    }

                    if (motion.State == (uint)MotionCode.Dead)
                    {
                        motion.EndFrame = unchecked(motion.EndFrame - DeadEndFrames);
                    }

                    last = slot;
                    model.MotionCount++;
                }
                else if (command.Length > 0 && command[0] == '*' && command.AsSpan().IndexOf(TalkWord) >= 0 && motionFiles.Count > 0)
                {
                    if (model.TalkMotionCount >= PristonFormatConstants.TalkMotionInfoCount)
                    {
                        throw new InvalidDataException($"{path} has more talk motions than a model-info file holds ({PristonFormatConstants.TalkMotionInfoCount}).");
                    }

                    var slot = (int)model.TalkMotionCount;
                    var talk = model.TalkMotions[slot];
                    ReadMotion(talk, ref cursor, model.Rates, Builder.TalkRates + slot);
                    talk.MotionFrame = motionFiles.Count - 1;
                    var name = Cp949.Encoding.GetString(command);
                    foreach (var (word, state) in TalkStates)
                    {
                        if (name.Contains(word, StringComparison.Ordinal))
                        {
                            talk.State = (uint)state;
                            break;
                        }
                    }

                    model.TalkMotionCount++;
                }

                continue;
            }

            switch (index)
            {
                case 0:
                    model.MotionFile = directory + cursor.NextQuotable();
                    break;
                case 1:
                case 17:
                    var file = cursor.NextQuotable();
                    motionFiles.Add(directory + file);
                    clipFrame = ClipFrame(path, file, cursor.NextQuotable(), motionFiles.Count);
                    break;
                case 2:
                    model.ModelFile = directory + cursor.NextText();
                    break;
                case 4:
                    model.LinkFile = directory + cursor.NextText();
                    break;
                case 5:
                    model.SubModelFile = directory + cursor.NextQuotable();
                    break;
                case 6:
                case 7:
                case 8:
                    var group = model.Groups[index - 6];
                    for (var name = 0; name < 4; name++)
                    {
                        var word = cursor.NextQuotable(out var present);
                        if (!present)
                        {
                            break;
                        }

                        if (group.Count < 4)
                        {
                            group.Add(word);
                        }
                    }

                    break;
                case 11:
                    ReadItems(model.Motions[last], ref cursor);
                    break;
                case 12:
                    var jobs = 0u;
                    while (cursor.NextText() is { Length: > 0 } job)
                    {
                        jobs |= PristonModelTextNames.JobBit(job);
                    }

                    model.Motions[last].JobCodeBit = jobs;
                    break;
                case 13:
                    var place = Place(cursor.NextText()) | Place(cursor.NextText());
                    model.Motions[last].MapPosition = place == 0 || model.Motions[last].State == (uint)MotionCode.Skill ? 3 : place;
                    break;
                case 14:
                    ReadSkills(model.Motions[last], ref cursor);
                    break;
                case 15:
                    model.TalkLinkFile = directory + cursor.NextQuotable();
                    break;
                case 16:
                    model.TalkMotionFile = directory + cursor.NextQuotable();
                    break;
            }
        }

        model.SelectNpcMotions();
        model.SelectTalkMotions();
        return model.Build(path);
    }

    // A motion line after its command: frames, events, loop or key, and (with a rate slot) its chance.
    private static void ReadMotion(Builder.Motion motion, ref Words cursor, int[] rates, int rate)
    {
        motion.StartFrame = (uint)Integer(cursor.NextText());
        motion.EndFrame = (uint)Integer(cursor.NextText());
        Array.Clear(motion.EventFrames);
        var events = 0;
        string word;
        while (true)
        {
            word = cursor.NextText();
            if (word.Length == 0 || word[0] is < '0' or > '9' || events >= motion.EventFrames.Length)
            {
                break;
            }

            motion.EventFrames[events++] = unchecked((uint)(Integer(word) - (int)motion.StartFrame) * Tick);
        }

        if (word == "반복")
        {
            motion.Repeat = 1;
        }

        if (word.Length > 0 && word[0] != '(' && word[0] is >= ' ' and <= '\x7F')
        {
            motion.KeyCode = (byte)word[0];
        }

        var chance = cursor.NextText();
        if (chance.Length is > 2 and < 6 && chance[0] == '(' && chance[^1] == ')')
        {
            rates[rate] = Math.Min(Integer(chance[1..^1]), RateCapacity);
        }
    }

    private static void ReadItems(Builder.Motion motion, ref Words cursor)
    {
        while (cursor.NextText() is { Length: > 0 } item)
        {
            if (item == "모두")
            {
                motion.ItemCodes.Clear();
                return;
            }

            var code = item == "없음" ? -1 : PristonModelTextNames.ItemIndex(item);
            if ((item == "없음" || code >= 0) && motion.ItemCodes.Count < PristonFormatConstants.MotionItemCapacity)
            {
                motion.ItemCodes.Add(code);
            }
        }
    }

    // A skill motion takes up to seven skills, the code after the last 0.
    private static void ReadSkills(Builder.Motion motion, ref Words cursor)
    {
        if (motion.State != (uint)MotionCode.Skill)
        {
            return;
        }

        var count = 0;
        while (cursor.NextText() is { Length: > 0 } skill)
        {
            var code = PristonModelTextNames.SkillCode(skill);
            if (code >= 0 && count < PristonFormatConstants.MotionSkillCapacity - 1)
            {
                motion.SkillCodes[count++] = (byte)code;
            }
        }

        motion.SkillCodes[count] = 0;
    }

    private static int Place(string word) => word switch
    {
        "마을" => 1,
        "필드" => 2,
        _ => 0
    };

    // The MotionFrame of the motions after a collection naming a glTF clip (0: by the collection's count).
    private static int ClipFrame(string descriptor, string file, string clip, int collection)
    {
        var extension = Path.GetExtension(file);
        if (!extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (clip.Length == 0)
        {
            return 0;
        }

        var model = GltfModelReader.Read(AssetFiles.Combine(Path.GetDirectoryName(descriptor) ?? "", file));
        for (var index = 0; index < model.Animations.Count; index++)
        {
            if (model.Animations[index].Name.Equals(clip, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        throw new InvalidDataException($"{descriptor}: {file} has no clip '{clip}' (collection {collection}).");
    }

    // Some shipped descriptors keep every byte XOR Masked: such a file opens with a masked comment, command or blank.
    private const byte Masked = 0x82;

    private static byte[] Plain(byte[] data)
    {
        static bool Opening(int value) => value is '/' or '*' or '\r' or '\n' or ' ' or '\t';
        if (data.Length == 0 || Opening(data[0]) || !Opening(data[0] ^ Masked))
        {
            return data;
        }

        var plain = new byte[data.Length];
        for (var index = 0; index < data.Length; index++)
        {
            plain[index] = (byte)(data[index] ^ Masked);
        }

        return plain;
    }

    // C's atoi: leading spaces, a sign, digits.
    private static int Integer(string word)
    {
        var index = 0;
        while (index < word.Length && char.IsWhiteSpace(word[index]))
        {
            index++;
        }

        var negative = index < word.Length && word[index] == '-';
        if (index < word.Length && word[index] is '-' or '+')
        {
            index++;
        }

        var value = 0L;
        while (index < word.Length && word[index] is >= '0' and <= '9')
        {
            value = Math.Min(value * 10 + (word[index++] - '0'), int.MaxValue + 1L);
        }

        return (int)(negative ? -value : Math.Min(value, int.MaxValue));
    }

    // Lines as a 512-byte line buffer reads them: up to the newline, at most LineCapacity bytes.
    private static IEnumerable<byte[]> Lines(byte[] data)
    {
        var start = 0;
        while (start < data.Length)
        {
            var end = start;
            while (end < data.Length && end - start < LineCapacity && data[end] != '\n')
            {
                end++;
            }

            if (end < data.Length && end - start < LineCapacity)
            {
                end++;
            }

            yield return data[start..end];
            start = end;
        }
    }

    // The words of one line: spaces and tabs separate them, the line ends at CR or LF.
    private ref struct Words(byte[] line)
    {
        private readonly byte[] _line = line;
        private int _position;

        public byte[] Next()
        {
            while (_position < _line.Length && _line[_position] is (byte)' ' or (byte)'\t')
            {
                _position++;
            }

            var start = _position;
            while (_position < _line.Length && _line[_position] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0))
            {
                _position++;
            }

            return _line[start.._position];
        }

        public string NextText() => Cp949.Encoding.GetString(Next());

        public string NextQuotable() => NextQuotable(out _);

        // A word opening with a quote is read again from where the word began, to the closing quote (or the line's end).
        public string NextQuotable(out bool present)
        {
            var from = _position;
            var word = Next();
            present = word.Length > 0;
            if (word.Length == 0 || word[0] != '"')
            {
                return Cp949.Encoding.GetString(word);
            }

            var open = Array.IndexOf(_line, (byte)'"', from);
            var start = open + 1;
            var end = start;
            while (end < _line.Length && _line[end] is not ((byte)'"' or (byte)'\r' or (byte)'\n' or 0))
            {
                end++;
            }

            _position = end < _line.Length && _line[end] == '"' ? end + 1 : end;
            return Cp949.Encoding.GetString(_line[start..end]);
        }
    }

    private sealed class Builder
    {
        public const int TalkRates = PristonFormatConstants.NpcMotionInfoCount + PristonFormatConstants.MotionRateSelectionCount;
        private const int NpcSelection = PristonFormatConstants.NpcMotionInfoCount;
        private const int TalkSelection = TalkRates + PristonFormatConstants.TalkMotionInfoCount;

        public Motion[] Motions { get; } = [.. Enumerable.Range(0, PristonFormatConstants.MotionInfoCount).Select(static _ => new Motion())];
        public Motion[] TalkMotions { get; } = [.. Enumerable.Range(0, PristonFormatConstants.TalkMotionInfoCount).Select(static _ => new Motion())];
        public List<string>[] Groups { get; } = [[], [], []];

        // The idle chances, their selection, the talk chances and the two talk files' selections, one table as the
        // model-info file lays them out: a chance past its table's end lands in the next.
        public int[] Rates { get; } = new int[TalkSelection + PristonFormatConstants.TalkMotionFileCount * PristonFormatConstants.MotionRateSelectionCount];
        public uint MotionCount { get; set; } = PristonFormatConstants.CharacterMotionBase;
        public uint TalkMotionCount { get; set; } = PristonFormatConstants.CharacterMotionBase;
        public string ModelFile { get; set; } = "";
        public string MotionFile { get; set; } = "";
        public string SubModelFile { get; set; } = "";
        public string LinkFile { get; set; } = "";
        public string TalkLinkFile { get; set; } = "";
        public string TalkMotionFile { get; set; } = "";

        public void SelectNpcMotions()
        {
            const int first = PristonFormatConstants.CharacterMotionBase;
            if (Rates[first] == 0)
            {
                return;
            }

            var (largest, at, sum) = (0, 0, 0);
            for (var slot = first; slot < MotionCount; slot++)
            {
                if (largest < Rates[slot])
                {
                    (largest, at) = (Rates[slot], slot);
                }

                sum += Rates[slot];
            }

            if (sum > RateCapacity)
            {
                largest -= sum - RateCapacity;
                if (largest <= 0)
                {
                    Rates[first] = 0;
                    return;
                }

                Rates[at] = largest;
            }
            else if (sum < RateCapacity)
            {
                Rates[at] = largest + RateCapacity - sum;
            }

            var next = NpcSelection;
            for (var slot = first; slot < MotionCount; slot++)
            {
                for (var share = 0; share < Rates[slot]; share++)
                {
                    Rates[next++] = slot;
                }
            }
        }

        // Each talk file shares its own 100 slots among its talk motions; the chances themselves stay as written.
        public void SelectTalkMotions()
        {
            const int first = PristonFormatConstants.CharacterMotionBase;
            if (Rates[TalkRates + first] == 0)
            {
                return;
            }

            var motions = new int[PristonFormatConstants.TalkMotionFileCount, PristonFormatConstants.TalkMotionInfoCount];
            var chances = new int[PristonFormatConstants.TalkMotionFileCount, PristonFormatConstants.TalkMotionInfoCount];
            int[] counts = [first, first];
            for (var slot = first; slot < TalkMotionCount; slot++)
            {
                var file = TalkMotions[slot].MotionFrame;
                if (file is TalkFile or FacialFile)
                {
                    motions[file, counts[file]] = slot;
                    chances[file, counts[file]] = Rates[TalkRates + slot];
                    counts[file]++;
                }
            }

            for (var file = 0; file < PristonFormatConstants.TalkMotionFileCount; file++)
            {
                var (largest, at, sum) = (0, 0, 0);
                for (var entry = first; entry < counts[file]; entry++)
                {
                    if (largest < chances[file, entry])
                    {
                        (largest, at) = (chances[file, entry], entry);
                    }

                    sum += chances[file, entry];
                }

                if (sum > RateCapacity)
                {
                    largest -= sum - RateCapacity;
                    if (largest <= 0)
                    {
                        Rates[TalkRates + first] = 0;
                    }
                    else
                    {
                        chances[file, at] = largest;
                    }
                }
                else if (sum < RateCapacity)
                {
                    chances[file, at] = largest + RateCapacity - sum;
                }

                if (Rates[TalkRates + first] == 0)
                {
                    continue;
                }

                var next = TalkSelection + file * PristonFormatConstants.MotionRateSelectionCount;
                for (var entry = first; entry < counts[file]; entry++)
                {
                    for (var share = 0; share < chances[file, entry]; share++)
                    {
                        Rates[next++] = motions[file, entry];
                    }
                }
            }
        }

        public PristonModelInfo Build(string path)
        {
            // Without a motion file, the model plays the motions of the model it links to, as PristonInxReader reads it.
            var motionFile = MotionFile.Length > 0 ? MotionFile : LinkFile;
            PristonModelGroup Group(int index) =>
                new(Groups[index].Count, [.. Groups[index], .. Enumerable.Repeat("", 4 - Groups[index].Count)]);

            int[] Table(int start, int count) => Rates[start..(start + count)];
            const int selection = PristonFormatConstants.MotionRateSelectionCount;
            return new PristonModelInfo(path, ModelFile, motionFile, SubModelFile, Group(0), Group(1), Group(2),
                [.. Motions.Select(static motion => motion.Build())], MotionCount, LinkFile, TalkLinkFile, TalkMotionFile,
                [.. TalkMotions.Select(static motion => motion.Build())], TalkMotionCount)
            {
                ModelKeywordsValid = true,
                NpcMotionRates = Table(0, PristonFormatConstants.NpcMotionInfoCount),
                NpcMotionSelection = Table(NpcSelection, selection),
                TalkMotionRates = Table(TalkRates, PristonFormatConstants.TalkMotionInfoCount),
                TalkMotionSelection = [Table(TalkSelection, selection), Table(TalkSelection + selection, selection)]
            };
        }

        public sealed class Motion
        {
            public uint State;
            public uint StartFrame;
            public uint EndFrame;
            public readonly uint[] EventFrames = new uint[4];
            public readonly List<int> ItemCodes = [];
            public uint JobCodeBit;
            public int MapPosition;
            public uint Repeat;
            public byte KeyCode;
            public int MotionFrame;
            public readonly byte[] SkillCodes = new byte[PristonFormatConstants.MotionSkillCapacity];

            public PristonMotionInfo Build()
            {
                var table = new int[PristonFormatConstants.MotionItemCapacity];
                ItemCodes.CopyTo(table);
                return new PristonMotionInfo(State, StartFrame, EndFrame, [.. EventFrames], ItemCodes.Count, JobCodeBit, MapPosition, Repeat, KeyCode,
                    MotionFrame)
                {
                    ItemIndices = [.. ItemCodes],
                    ItemCodeTable = table,
                    SkillCodes = [.. SkillCodes],
                    RawStartFrame = StartFrame,
                    RawEndFrame = EndFrame
                };
            }
        }
    }
}
