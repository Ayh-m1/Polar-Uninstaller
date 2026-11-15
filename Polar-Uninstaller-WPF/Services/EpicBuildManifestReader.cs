using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PolarUninstallerWpf.Services;

public sealed record EpicInstalledFile(string RelativePath,long Size,string Sha1);
public sealed record EpicBuildFiles(string AppName,string LaunchExecutable,IReadOnlyList<EpicInstalledFile> Files,string BuildVersion="");

/// <summary>Bounded, read-only parser for unencrypted BuildPatch binary manifests
/// (serialization 17–21). Reads install metadata and file identities, not chunks.
/// Format reference: legendary-gl/legendary, models/manifest.py. No external tool
/// or Python runtime is used. Unknown formats fail closed.</summary>
public static class EpicBuildManifestReader
{
    public static EpicBuildFiles Read(byte[] bytes)
    {
        const int limit=128*1024*1024;
        using var input=new MemoryStream(bytes,false);using var header=new BinaryReader(input);
        if(bytes.Length<41||bytes.Length>limit||header.ReadUInt32()!=0x44BEC00C)throw new InvalidDataException("Unsupported Epic build manifest.");
        int headerSize=checked((int)header.ReadUInt32()),expanded=checked((int)header.ReadUInt32()),stored=checked((int)header.ReadUInt32());
        byte[] digest=header.ReadBytes(20);byte flags=header.ReadByte();uint version=header.ReadUInt32();
        if(headerSize!=41||version is <17 or >21||flags>1||expanded<1||expanded>limit||stored!=bytes.Length-headerSize)
            throw new InvalidDataException("Unsupported or incomplete Epic build manifest.");
        input.Position=headerSize;byte[] body=new byte[expanded];
        if(flags==1)
        {
            using var zip=new ZLibStream(input,CompressionMode.Decompress,true);zip.ReadExactly(body);
            if(zip.ReadByte()!=-1)throw new InvalidDataException("Epic manifest exceeds its declared size.");
        }
        else{if(stored!=expanded)throw new InvalidDataException("Invalid manifest size.");input.ReadExactly(body);}
        if(!CryptographicOperations.FixedTimeEquals(SHA1.HashData(body),digest))throw new InvalidDataException("Epic manifest checksum mismatch.");
        using var data=new MemoryStream(body,false);using var reader=new BinaryReader(data);
        long Section(){long start=data.Position;uint length=reader.ReadUInt32();if(length<5||length>data.Length-start)throw new InvalidDataException("Invalid manifest section.");return start+length;}
        int Count(){uint n=reader.ReadUInt32();if(n>1_000_000)throw new InvalidDataException("Excessive manifest count.");return (int)n;}
        void Bound(long end){if(data.Position>end)throw new InvalidDataException("Truncated manifest section.");}
        string Text(long end)
        {
            int n=reader.ReadInt32();if(n==0)return "";
            long length=n<0?-(long)n*2:n;
            if(length<1||length>1024*1024||length>end-data.Position)throw new InvalidDataException("Invalid manifest string.");
            byte[] value=reader.ReadBytes((int)length);int terminator=n<0?2:1;
            if(value.Length!=length||value[^1]!=0||(terminator==2&&value[^2]!=0))throw new InvalidDataException("Unterminated manifest string.");
            Encoding encoding=n<0?new UnicodeEncoding(false,false,true):new UTF8Encoding(false,true);
            return encoding.GetString(value,0,value.Length-terminator);
        }
        long metaEnd=Section();byte metaVersion=reader.ReadByte();uint feature=reader.ReadUInt32();bool fileData=reader.ReadByte()!=0;reader.ReadUInt32();
        if(metaVersion>2||feature>21||fileData)throw new InvalidDataException("Unsupported Epic install metadata.");
        string app=Text(metaEnd);string buildVersion=Text(metaEnd);string launch=Text(metaEnd);Bound(metaEnd);data.Position=metaEnd;
        long chunksEnd=Section();data.Position=chunksEnd;
        long filesEnd=Section();byte fileVersion=reader.ReadByte();int count=Count();
        if(fileVersion>2||count==0)throw new InvalidDataException("Unsupported Epic file list.");
        var names=new string[count];var hashes=new string[count];var sizes=new long[count];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<count;i++)
        {
            string name=Text(filesEnd).Replace('/', '\\');
            if(name.Length==0||Path.IsPathRooted(name)||name.Contains(':')||name.Split('\\').Any(x=>x is "" or "." or ".."||x.EndsWith(' ')||x.EndsWith('.')||x.IndexOfAny(Path.GetInvalidFileNameChars())>=0||
                System.Text.RegularExpressions.Regex.IsMatch(x,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))||!seen.Add(name))
                throw new InvalidDataException("Unsafe or duplicate Epic file path.");
            names[i]=name;
        }
        for(int i=0;i<count;i++)if(Text(filesEnd).Length!=0)throw new InvalidDataException("Epic symlink manifests are unsupported.");
        for(int i=0;i<count;i++){var hash=reader.ReadBytes(20);if(hash.Length!=20)throw new EndOfStreamException();hashes[i]=Convert.ToHexString(hash);}
        for(int i=0;i<count;i++)reader.ReadByte();
        for(int i=0;i<count;i++){int tags=Count();for(int j=0;j<tags;j++)_ = Text(filesEnd);Bound(filesEnd);}
        for(int i=0;i<count;i++)
        {
            int parts=Count();
            for(int j=0;j<parts;j++)
            {
                long start=data.Position;uint length=reader.ReadUInt32();
                if(length<28||length>filesEnd-start)throw new InvalidDataException("Invalid Epic file chunk range.");
                data.Position+=20;sizes[i]=checked(sizes[i]+reader.ReadUInt32());data.Position=start+length;
            }
            Bound(filesEnd);
        }
        // Optional v1/v2 hashes are not needed: the original SHA-1 is verified
        // against file contents before deletion, as required by this format.
        Bound(filesEnd);
        return new(app,launch,names.Select((name,i)=>new EpicInstalledFile(name,sizes[i],hashes[i])).ToArray(),buildVersion);
    }
}
