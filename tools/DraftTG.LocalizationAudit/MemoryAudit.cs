using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DraftTG.LocalizationAudit;

internal sealed record ReadSpan(string Reason, string Address, int Requested, int Read);
internal sealed record CandidateHits(int CardId, string Encoding, int Count, IReadOnlyList<string> Addresses);
internal static class MemoryAudit
{
    // These are audit resource limits, not runtime layout offsets or signatures.
    private const int MaxBytes = 48*1024*1024, MaxQueries = 8192;
    public static object Read(Process process, int[] candidates)
    {
        if (candidates.Length is < 1 or > 14) throw new InvalidOperationException("No bounded active pack in current Player.log; memory search skipped.");
        using var handle = OpenProcess(0x410, false, process.Id); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ only.
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var watch = Stopwatch.StartNew(); var readBytes=0; var queries=0; var failures=0;
        var spans = new List<ReadSpan>(); var counts = new Dictionary<(int,string),List<string>>(); var totals = new Dictionary<(int,string),int>();
        var ids = candidates.Distinct().ToArray(); var controls = ids.Select(i=>checked(i+100_000_000)).ToArray();
        var samples = new List<(ulong Address, byte[] Bytes)>();
        var pointers = new Dictionary<ulong,(MemoryRegion Region, HashSet<ulong> Targets)>();
        var queriedPointers = new HashSet<ulong>();
        bool Query(ulong address,out MemoryRegion region)
        { region=default; queries++; return queries <= MaxQueries && watch.Elapsed < TimeSpan.FromSeconds(8)
            && VirtualQueryEx(handle,(nint)address,out region,(nuint)Marshal.SizeOf<MemoryRegion>())!=0; }
        byte[] Read(ulong address,int length,string reason)
        {
            length=Math.Min(length,MaxBytes-readBytes); if(length<=0 || watch.Elapsed>TimeSpan.FromSeconds(8)) return [];
            var buffer=new byte[length];
            if(!ReadProcessMemory(handle,(nint)address,buffer,(nuint)length,out var read)) failures++;
            var size=checked((int)read); readBytes+=length; spans.Add(new(reason,$"0x{address:X}",length,size));
            if(size!=length) Array.Resize(ref buffer,size);
            foreach(var id in ids.Concat(controls))
            foreach(var encoding in new[]{"Int32LE","ASCII","UTF16LE"})
            {
                var needle=encoding switch { "Int32LE"=>BitConverter.GetBytes(id),"ASCII"=>Encoding.ASCII.GetBytes(id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    _=>Encoding.Unicode.GetBytes(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
                var offset=0; var key=(id,encoding);
                while(offset<=buffer.Length-needle.Length)
                {
                    var found=buffer.AsSpan(offset).IndexOf(needle); if(found<0) break; var at=offset+found;
                    totals[key]=totals.GetValueOrDefault(key)+1;
                    if(!counts.TryGetValue(key,out var locations)) counts[key]=locations=[];
                    if(locations.Count<32) locations.Add($"0x{address+(ulong)at:X}");
                    offset=at+needle.Length;
                }
            }
            return buffer;
        }
        foreach(var module in process.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName is "UnityPlayer.dll" or "mono-2.0-bdwgc.dll")
            .OrderBy(m=>m.ModuleName.StartsWith("mono-",StringComparison.Ordinal)?0:1))
        {
            using var file=File.OpenRead(module.FileName); using var pe=new PEReader(file);
            foreach(var section in pe.PEHeaders.SectionHeaders.Where(s=>(s.SectionCharacteristics & SectionCharacteristics.MemWrite)!=0))
            {
                var address=(ulong)module.BaseAddress.ToInt64()+(uint)section.VirtualAddress;
                var bytes=Read(address,Math.Min(section.VirtualSize,2*1024*1024),module.ModuleName+":"+section.Name);
                samples.Add((address,bytes));
            }
        }
        // One-hop OS region selection from pointers in the runtime's writable sections. No pointer-chain/type-layout assumptions.
        foreach(var sample in samples)
        for(var offset=0;offset+8<=sample.Bytes.Length;offset+=8)
        {
            if(queries>=MaxQueries || watch.Elapsed>TimeSpan.FromSeconds(8)) break;
            var value=BinaryPrimitives.ReadUInt64LittleEndian(sample.Bytes.AsSpan(offset,8));
            if(value<0x10000 || value>0x7fff_ffff_ffff || (value&7)!=0) continue;
            if(!queriedPointers.Add(value)) continue;
            if(!Query(value,out var region) || !Readable(region) || region.Type!=0x20000 || region.RegionSize<64*1024) continue;
            if(!pointers.TryGetValue(region.BaseAddress,out var selected)) selected=(region,[]);
            selected.Targets.Add(value); pointers[region.BaseAddress]=selected;
        }
        foreach(var selected in pointers.Values.OrderByDescending(p=>p.Targets.Count).Take(16))
        {
            // At most 2 MiB per selected region, centered on a runtime-referenced address; never sweep the whole process.
            var target=selected.Targets.Min(); var address=Math.Max(selected.Region.BaseAddress,target>65536?target-65536:target);
            var available=selected.Region.BaseAddress+selected.Region.RegionSize-address;
            Read(address,(int)Math.Min(available,2*1024*1024),$"runtime-referenced private region ({selected.Targets.Count} distinct pointer targets; not verified Mono heap)");
        }
        CandidateHits Hit(int id,string encoding)=>new(id,encoding,totals.GetValueOrDefault((id,encoding)),counts.GetValueOrDefault((id,encoding))??[]);
        return new { AccessMask="0x410 (query + VM read)", BudgetBytes=MaxBytes, RequestedBytes=readBytes,
            Queries=queries, ReadFailures=failures, DurationMs=watch.Elapsed.TotalMilliseconds, Spans=spans,
            SelectedPrivateRegions=pointers.Count, Candidates=ids.SelectMany(i=>new[]{"Int32LE","ASCII","UTF16LE"}.Select(e=>Hit(i,e))).ToArray(),
            NegativeControls=controls.SelectMany(i=>new[]{"Int32LE","ASCII","UTF16LE"}.Select(e=>Hit(i,e))).ToArray(),
            IdToUiRectangle="Not established. Matches are raw occurrences, not typed objects or live rectangles.",
            Limitations="Bounded sample only. Text may be log/network/catalog data; integers may be unrelated. No raw bytes or surrounding text are persisted." };
    }
    private static bool Readable(MemoryRegion r)=>r.State==0x1000 && (r.Protect&0x100)==0 && (r.Protect&0xff) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    [StructLayout(LayoutKind.Sequential)] private struct MemoryRegion
    { public ulong BaseAddress,AllocationBase; public uint AllocationProtect; public ushort PartitionId; public ulong RegionSize; public uint State,Protect,Type; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nuint VirtualQueryEx(SafeProcessHandle process,nint address,out MemoryRegion region,nuint length);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint length,out nuint read);
}
