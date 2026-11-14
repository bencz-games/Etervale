using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class BinaryCursorTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("binary cursor preserves fixed string fields and little-endian widths", FieldsAsync);
        yield return ("binary cursor rejects negative positions, overflow and truncated fields", BoundsAsync);
        yield return ("binary matrix parsing preserves scalar conversion and axis mapping", MatricesAsync);
        yield return ("render clock preserves sub-millisecond frames and original uint wrap", ClockAsync);
        yield return ("INX reader preserves complete fixed-width motion records", InxLayoutAsync);
        yield return ("SMD reader accepts verified model signatures and preserves stale file trailers", ModelLayoutsAsync);
        yield return ("SMD stage reader decodes classic WORD and EU DWORD area indexes", StageAreaLayoutsAsync);
        yield return ("CAM reader preserves Win32 rotation records and uses incremental quaternions", CameraLayoutAsync);
    }

    private static Task FieldsAsync()
    {
        byte[] bytes = [65, 0, 66, 255, 67, 68, 0, 255, 255, 255, 128, 0, 0, 192, 63];
        var cursor = new BinaryCursor(bytes);
        Assert.True(cursor.FixedString(4) == "A" && cursor.Position == 4, "NUL must not shorten field advancement");
        Assert.True(cursor.FixedString(2) == "CD", "unterminated fixed string lost its tail");
        Assert.True(cursor.FixedString(0) == string.Empty && cursor.Position == 6, "empty field advanced the cursor");
        Assert.True(cursor.UInt16() == 0xFF00 && cursor.Int16() == -1, "16-bit signed/unsigned decode changed");
        Assert.True(cursor.Byte() == 128 && cursor.Single() == 1.5f, "float or byte decode changed");
        cursor = new BinaryCursor([0x78, 0x56, 0x34, 0xF2]);
        Assert.True(cursor.UInt32() == 0xF2345678u, "32-bit field is not little endian");
        cursor.Position = 0;
        Assert.True(cursor.Int32() == unchecked((int)0xF2345678u), "signed 32-bit field lost the sign bit");
        cursor = new BinaryCursor([255, 65]);
        Assert.True(cursor.FixedString(2) == Encoding.ASCII.GetString([255, 65]), "ASCII fallback differs from original decoding");
        return Task.CompletedTask;
    }

    private static Task BoundsAsync()
    {
        var cursor = new BinaryCursor(new byte[8]);
        foreach (var position in new[] { int.MinValue, -1, 9, int.MaxValue })
        {
            cursor.Position = position;
            ExpectInvalid(() => cursor.Require(0));
        }

        cursor.Position = 1;
        ExpectInvalid(() => cursor.Require(int.MaxValue));
        ExpectInvalid(() => cursor.FixedString(8));
        Assert.True(cursor.Position == 1, "failed field advanced the cursor");
        cursor.Position = 8;
        cursor.Require(0);
        ExpectInvalid(() => cursor.Byte());
        return Task.CompletedTask;
    }

    private static Task MatricesAsync()
    {
        var fixedBytes = new byte[64];
        var floatBytes = new byte[64];
        var scalar = new float[16];
        var axis = new Matrix(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
        var random = new Random(481);
        for (var sample = 0; sample < 100; sample++)
        {
            for (var index = 0; index < 16; index++)
            {
                var value = random.Next(-10_000_000, 10_000_000);
                scalar[index] = value / 256f;
                BinaryPrimitives.WriteInt32LittleEndian(fixedBytes.AsSpan(index * 4, 4), value);
                BinaryPrimitives.WriteSingleLittleEndian(floatBytes.AsSpan(index * 4, 4), scalar[index]);
            }

            var original = new Matrix(scalar[0], scalar[1], scalar[2], scalar[3], scalar[4], scalar[5], scalar[6], scalar[7],
                scalar[8], scalar[9], scalar[10], scalar[11], scalar[12], scalar[13], scalar[14], scalar[15]);
            var reference = Matrix.Invert(axis) * original * axis;
            var cursor = new BinaryCursor(fixedBytes);
            Assert.True(PristonBinaryUtil.ReadFixedMatrix(cursor) == reference && cursor.Position == 64, "fixed matrix differs from scalar reference");
            Assert.True(PristonBinaryUtil.ReadFloatMatrix(new BinaryCursor(floatBytes)) == reference, "float matrix differs from scalar reference");
        }

        ExpectInvalid(() => PristonBinaryUtil.ReadFixedMatrix(new BinaryCursor(new byte[63])));
        ExpectInvalid(() => PristonBinaryUtil.ReadFloatMatrix(new BinaryCursor(new byte[63])));
        return Task.CompletedTask;
    }

    private static Task ClockAsync()
    {
        var clock = new PristonRenderClock();
        for (var frame = 0; frame < 2000; frame++)
        {
            clock.Advance(TimeSpan.FromTicks(5000));
        }

        Assert.True(clock.Milliseconds == 1000, "2000 FPS froze or slowed the render clock");
        clock.Advance(TimeSpan.FromTicks(-1));
        clock.Advance(TimeSpan.Zero);
        Assert.True(clock.Milliseconds == 1000, "negative delta reversed the render clock");
        clock.Advance(TimeSpan.FromTicks(((long)uint.MaxValue - 999) * TimeSpan.TicksPerMillisecond));
        Assert.True(clock.Milliseconds == 0, "original millisecond counter did not wrap modulo 2^32");
        clock.Advance(TimeSpan.FromTicks(9999));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(clock.Milliseconds == 1, "fractional remainder was discarded");
        return Task.CompletedTask;
    }

    private static Task InxLayoutAsync()
    {
        const int motionOffset = 396;
        const int motionSize = 172;
        var bytes = new byte[95_268];
        WriteAscii(bytes, 0, 64, "char\\tmABCD\\tmab.inx");
        WriteInt32(bytes, 192, 1);
        WriteAscii(bytes, 196, 16, "BodyH");

        var motion = motionOffset;
        WriteUInt32(bytes, motion, (uint)MotionCode.Stand);
        WriteUInt32(bytes, motion + 4, 0x11223344u);
        WriteUInt32(bytes, motion + 8, 7);
        WriteUInt32(bytes, motion + 12, 0x55667788u);
        WriteUInt32(bytes, motion + 16, 13);
        WriteInt32(bytes, motion + 36, 2);
        WriteInt16(bytes, motion + 40, -1);
        WriteInt16(bytes, motion + 42, 725);
        bytes[motion + 148] = 3;
        bytes[motion + 155] = 9;
        bytes[motion + 164] = 81;

        var motionCountOffset = motionOffset + PristonFormatConstants.MotionInfoCount * motionSize;
        WriteUInt32(bytes, motionCountOffset, 10);
        WriteUInt32(bytes, motionCountOffset + 4, 0xAABBCCDDu);
        WriteUInt32(bytes, motionCountOffset + 8, 0x10203040u);
        var talkMotionCountOffset = motionCountOffset + 12 + 3 * 64 + PristonFormatConstants.TalkMotionInfoCount * motionSize;
        WriteUInt32(bytes, talkMotionCountOffset, 10);

        var path = WriteTemporaryAsset(".inx", bytes);
        try
        {
            var info = new PristonInxReader().Read(path);
            var parsedMotion = info.Motions[0];
            Assert.True(info.HighModel.ModelNameCount == 1 && info.HighModel.ModelNames[0] == "BodyH", "INX model group changed");
            Assert.True(info.FileTypeKeyword == 0xAABBCCDDu && info.LinkFileKeyword == 0x10203040u, "INX keyword fields were discarded");
            Assert.True(parsedMotion.ItemIndices.SequenceEqual([-1, 725]), "signed modern ItemCodeList entries changed");
            Assert.True(parsedMotion.ItemCodeTable.Count == PristonFormatConstants.MotionItemCapacity, "fixed ItemCodeList tail was discarded");
            Assert.True(parsedMotion.SkillCodes.Count == PristonFormatConstants.MotionSkillCapacity && parsedMotion.SkillCodes[0] == 3 && parsedMotion.SkillCodes[7] == 9,
                "SkillCode table changed");
            Assert.True(parsedMotion.MotionKeyword1 == 0x11223344u && parsedMotion.MotionKeyword2 == 0x55667788u, "motion keywords were discarded");
            Assert.True(parsedMotion.RawStartFrame == 7 && parsedMotion.RawEndFrame == 13, "raw frame words were discarded");
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static Task ModelLayoutsAsync()
    {
        byte[][] headers =
        [
            Encoding.ASCII.GetBytes("SMD Model data Ver 0.62\0"),
            [0xA5, 0x82, 0xAC, 0x58, 0x15, 0x98, 0x29, 0x8C, 0x85, 0x9F, 0x12, 0xC8, 0x84, 0x1F, 0x74, 0x8C, 0x8F, 0x42, 0x8F, 0xA8, 0x1B, 0x84, 0xAA, 0x00],
            Encoding.ASCII.GetBytes("SMD Stage data Ver 0.72\0")
        ];

        foreach (var header in headers)
        {
            var binaryModel = header[0] == 0xA5;
            Assert.True(PristonSmdReader.HasBinaryModelSignature(header) == binaryModel,
                "binary model signature detection differs from the model reader");
            if (binaryModel)
            {
                Assert.True(!PristonSmdReader.HasBinaryModelSignature(header.AsSpan(0, header.Length - 1)),
                    "a truncated binary signature was accepted");
                var corrupted = (byte[])header.Clone();
                corrupted[0] ^= 0x01;
                Assert.True(!PristonSmdReader.HasBinaryModelSignature(corrupted),
                    "a damaged binary signature was accepted");
            }

            var bytes = CreateMinimalModel(header, [0xDE, 0xAD, 0xBE, 0xEF]);
            var path = WriteTemporaryAsset(binaryModel ? ".smb" : ".smd", bytes);
            try
            {
                var model = new PristonSmdReader().ReadModel(path);
                Assert.True(model.Objects.Count == 1 && model.Objects[0].Name == "Root", "model object block changed");
                Assert.True(model.Objects[0].SourceOffset == 596 && model.Objects[0].SourceLength == 2236, "object-info offsets were not preserved");
                Assert.True(model.SourceTrailer.ByteCount == 4 && model.SourceTrailer.Fnv1a32 == 0x045D4BB3u, "OPEN_ALWAYS trailer metadata changed");
                if (header[4] == (byte)'S')
                {
                    Assert.True(model.Header.Kind == PristonSmdHeaderKind.StageHeaderWithModelLayout, "stage-signature model was misclassified");
                    ExpectInvalid(() => new PristonSmdReader().ReadStage(path));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private static Task StageAreaLayoutsAsync()
    {
        foreach (var elementWidth in new[] { sizeof(ushort), sizeof(uint) })
        {
            var bytes = CreateMinimalStage(elementWidth);
            var path = WriteTemporaryAsset(".smd", bytes);
            try
            {
                var stage = new PristonSmdReader().ReadStage(path);
                Assert.True(stage.AreaIndex.SerializedElementWidth == elementWidth, "stage-area element width was misclassified");
                Assert.True(stage.AreaIndex.OccupiedCellCount == 1 && stage.AreaIndex.IndexedFaceReferenceCount == 1, "stage-area counts changed");
                Assert.True(stage.AreaIndex.GetFaces(3, 5).SequenceEqual([0]), "stage-area face index changed");
                Assert.True(stage.AreaIndex.GetFaces(259, 261).SequenceEqual([0]), "stage-area coordinate wrapping changed");
                Assert.True(stage.SourceTrailer.ByteCount == 0, "valid stage payload left unparsed bytes");
            }
            finally
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private static Task CameraLayoutAsync()
    {
        var bytes = CreateCamera();
        var path = WriteTemporaryAsset(".cam", bytes);
        try
        {
            var camera = PristonCameraPath.ReadCam(path);
            Assert.True(camera.InitialFrame == 23 && camera.LastFrame == 160, "camera header fields changed");
            Assert.True(camera.PositionKeys.Count == 2 && camera.RotationKeys.Count == 2, "camera key counts changed");
            Assert.True(camera.PositionKeys[1] == new PristonCameraPositionKey(160, new Vector3(5f, 6f, 7f)), "position key fields changed");

            var rotation = camera.RotationKeys[0];
            Assert.True(rotation.Angles == new Int3(11, 12, 13), "SRotAngle fields were discarded");
            Assert.True(rotation.QuaternionAlignmentPadding32 == 0xAABBCCDDu, "rotation key alignment bytes were discarded");
            Assert.True(rotation.IncrementalQuaternion == new PristonDoubleQuaternion(0d, 0d, 0d, 1d), "rotation key doubles changed");

            camera.Sample(80f, out var position, out var forward, out var up);
            Assert.True(position == new Vector3(3f, 4f, -5f), "camera position interpolation changed");
            Assert.True(Vector3.Distance(forward, Vector3.Forward) > 0.1f, "incremental quaternion was not applied");
            Assert.True(MathF.Abs(Vector3.Dot(forward, up)) < 1e-5f, "camera basis is not orthogonal");

            var oversized = bytes.Concat(new byte[] { 1 }).ToArray();
            var oversizedPath = WriteTemporaryAsset(".cam", oversized);
            try
            {
                ExpectInvalid(() => PristonCameraPath.ReadCam(oversizedPath));
            }
            finally
            {
                File.Delete(oversizedPath);
            }
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static byte[] CreateCamera()
    {
        const int positionCount = 2;
        const int rotationCount = 2;
        var bytes = new byte[16 + positionCount * 16 + rotationCount * (16 + 40 + 64)];
        WriteInt32(bytes, 0, 23);
        WriteInt32(bytes, 4, 160);
        WriteInt32(bytes, 8, positionCount);
        WriteInt32(bytes, 12, rotationCount);

        WriteCameraPosition(bytes, 16, 0, 1f, 2f, 3f);
        WriteCameraPosition(bytes, 32, 160, 5f, 6f, 7f);

        var angleOffset = 48;
        WriteCameraAngle(bytes, angleOffset, 0, 11, 12, 13);
        WriteCameraAngle(bytes, angleOffset + 16, 160, 21, 22, 23);

        var quaternionOffset = angleOffset + rotationCount * 16;
        WriteCameraQuaternion(bytes, quaternionOffset, 0, 0xAABBCCDDu, 0d, 0d, 0d, 1d);
        var halfSqrt = Math.Sqrt(0.5d);
        WriteCameraQuaternion(bytes, quaternionOffset + 40, 160, 0, 0d, 0d, halfSqrt, halfSqrt);

        var matrixOffset = quaternionOffset + rotationCount * 40;
        WriteFixedIdentity(bytes, matrixOffset);
        WriteFixedIdentity(bytes, matrixOffset + 64);
        return bytes;
    }

    private static void WriteCameraPosition(byte[] destination, int offset, int frame, float x, float y, float z)
    {
        WriteInt32(destination, offset, frame);
        WriteSingle(destination, offset + 4, x);
        WriteSingle(destination, offset + 8, y);
        WriteSingle(destination, offset + 12, z);
    }

    private static void WriteCameraAngle(byte[] destination, int offset, int frame, int x, int y, int z)
    {
        WriteInt32(destination, offset, frame);
        WriteInt32(destination, offset + 4, x);
        WriteInt32(destination, offset + 8, y);
        WriteInt32(destination, offset + 12, z);
    }

    private static void WriteCameraQuaternion(
        byte[] destination,
        int offset,
        int frame,
        uint padding,
        double x,
        double y,
        double z,
        double w)
    {
        WriteInt32(destination, offset, frame);
        WriteUInt32(destination, offset + 4, padding);
        WriteDouble(destination, offset + 8, x);
        WriteDouble(destination, offset + 16, y);
        WriteDouble(destination, offset + 24, z);
        WriteDouble(destination, offset + 32, w);
    }

    private static void WriteFixedIdentity(byte[] destination, int offset)
    {
        WriteInt32(destination, offset, 32768);
        WriteInt32(destination, offset + 20, 32768);
        WriteInt32(destination, offset + 40, 32768);
        WriteInt32(destination, offset + 60, 32768);
    }

    private static byte[] CreateMinimalModel(byte[] header, byte[] trailer)
    {
        const int headerSize = 556;
        const int objectInfoSize = 40;
        const int objectSize = 2236;
        var objectOffset = headerSize + objectInfoSize;
        var bytes = new byte[objectOffset + objectSize + trailer.Length];
        header.CopyTo(bytes, 0);
        WriteInt32(bytes, 24, 1);
        WriteInt32(bytes, 28, 0);
        WriteInt32(bytes, 32, objectOffset);
        WriteInt32(bytes, 36, objectOffset);
        WriteAscii(bytes, headerSize, 32, "Root");
        WriteInt32(bytes, headerSize + 32, objectSize);
        WriteInt32(bytes, headerSize + 36, objectOffset);
        WriteUInt32(bytes, objectOffset, 0xC1424344u);
        WriteAscii(bytes, objectOffset + 172, 32, "Root");
        trailer.CopyTo(bytes, objectOffset + objectSize);
        return bytes;
    }

    private static byte[] CreateMinimalStage(int elementWidth)
    {
        const int headerSize = 556;
        const int stageSize = 262_260;
        const int faceSize = 28;
        var payloadSize = sizeof(int) + 2 * elementWidth;
        var bytes = new byte[headerSize + stageSize + faceSize + payloadSize];
        Encoding.ASCII.GetBytes("SMD Stage data Ver 0.72\0").CopyTo(bytes, 0);
        var stageOffset = headerSize;
        var cell = 3 * PristonStageAreaIndex.Width + 5;
        WriteUInt32(bytes, stageOffset + sizeof(uint) + cell * sizeof(uint), 1);
        WriteInt32(bytes, stageOffset + 262_200, 1);
        var payloadOffset = headerSize + stageSize + faceSize;
        WriteInt32(bytes, payloadOffset, 2);
        if (elementWidth == sizeof(uint))
        {
            WriteUInt32(bytes, payloadOffset + sizeof(int), 1);
            WriteUInt32(bytes, payloadOffset + sizeof(int) + sizeof(uint), 0);
        }
        else
        {
            WriteUInt16(bytes, payloadOffset + sizeof(int), 1);
            WriteUInt16(bytes, payloadOffset + sizeof(int) + sizeof(ushort), 0);
        }

        return bytes;
    }

    private static string WriteTemporaryAsset(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"priston-format-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void WriteAscii(byte[] destination, int offset, int capacity, string value)
    {
        var encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length > capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        encoded.CopyTo(destination, offset);
    }

    private static void WriteInt16(byte[] destination, int offset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(destination.AsSpan(offset, sizeof(short)), value);
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset, sizeof(ushort)), value);
    }

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, sizeof(int)), value);
    }

    private static void WriteUInt32(byte[] destination, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteSingle(byte[] destination, int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination.AsSpan(offset, sizeof(float)), value);
    }

    private static void WriteDouble(byte[] destination, int offset, double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(destination.AsSpan(offset, sizeof(double)), value);
    }

    private static void ExpectInvalid(Action operation)
    {
        try
        {
            operation();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("Expected InvalidDataException for malformed binary field.");
    }
}
