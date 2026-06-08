using System.IO;

namespace SplitPlay.Launch;

/// <summary>The CPU architecture an executable targets.</summary>
public enum ProcessArchitecture
{
    Unknown,
    X86,
    X64
}

/// <summary>
/// Reads an executable's PE header to determine whether it is 32- or 64-bit. We
/// need this so we copy the matching (x86/x64) XInput proxy next to the game.
/// </summary>
public static class PeArchitectureReader
{
    private const int PeSignatureOffsetLocation = 0x3C;
    private const ushort MachineX86 = 0x014C;
    private const ushort MachineX64 = 0x8664;

    public static ProcessArchitecture Read(string exePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(exePath);
            using var reader = new BinaryReader(stream);

            // The PE header offset is stored at 0x3C in the DOS header.
            stream.Seek(PeSignatureOffsetLocation, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();

            stream.Seek(peOffset, SeekOrigin.Begin);
            uint peSignature = reader.ReadUInt32(); // "PE\0\0"
            if (peSignature != 0x0000_4550)
            {
                return ProcessArchitecture.Unknown;
            }

            // The Machine field is the first 2 bytes of the COFF file header,
            // immediately after the 4-byte PE signature.
            ushort machine = reader.ReadUInt16();
            return machine switch
            {
                MachineX64 => ProcessArchitecture.X64,
                MachineX86 => ProcessArchitecture.X86,
                _ => ProcessArchitecture.Unknown
            };
        }
        catch (IOException)
        {
            return ProcessArchitecture.Unknown;
        }
    }
}
