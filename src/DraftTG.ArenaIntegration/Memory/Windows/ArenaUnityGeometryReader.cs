using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace DraftTG.ArenaIntegration.Memory.Windows;

/// <summary>Passive cached Unity values, for the already pinned UnityPlayer only. Never invokes a getter.</summary>
internal sealed class ArenaUnityGeometryReader(IRemoteMemory memory, ArenaMonoRuntime runtime, string directory, ulong unityPlayerBase)
{
    private readonly Dictionary<string,Guid> identities = [];
    private readonly List<(ulong Address, byte[] Bytes)> observed = [];
    private readonly Dictionary<int,ulong> cameras = [];
    private readonly List<Action> managedChecks = [];
    internal void Begin() {observed.Clear();managedChecks.Clear();}
    private void Assembly(ulong obj, string name)
    {
        if(!identities.TryGetValue(name,out var id))
        {
            var expected = name switch
            {
                "UnityEngine.CoreModule" => "7C8FAF1B9BEFE71EC2D2A5AAE0ACBB97C6FD8962D9514495D701F717A20B0014",
                "UnityEngine.UIModule" => "DDF3665576C653F9E6F7B2B5F12569DFA1E81B3793FB5A7E8C912EAE1141BD84",
                "UnityEngine.UI" => "4BF50572EE01F1A62147142CC9750058EC01DFB3790B46BE9B42D207A4BD3797",
                _ => throw new ArenaMemoryException(ArenaMemoryStatus.UnsupportedBuild,"Unsupported UI assembly")
            };
            using var stream=File.OpenRead(Path.Combine(directory,name+".dll"));
            if(Convert.ToHexString(SHA256.HashData(stream))!=expected) Fail("Unknown supplemental UI assembly hash");
            stream.Position=0;using var pe=new PEReader(stream);id=pe.GetMetadataReader().GetGuid(pe.GetMetadataReader().GetModuleDefinition().Mvid);
            identities[name]=id;
        }
        runtime.ExpectAssembly(obj,name,id);
    }
    private byte[] Read(ulong address,int count)
    { var bytes=memory.Read(address,count);observed.Add((address,bytes));return bytes; }
    private ulong Reference(ulong obj,string field)=>BitConverter.ToUInt64(Read(runtime.Address(obj,field),8));
    private ulong Ptr(ulong address)
    { var p=System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Read(address,8));RemoteMemory.Validate(p,1);return p; }
    private int Int(ulong address)=>BitConverter.ToInt32(Read(address,4));
    private float Float(ulong address)=>BitConverter.ToSingle(Read(address,4));
    private ulong Native(ulong obj,string assembly)
    { Assembly(obj,assembly);return Ptr(runtime.Address(obj,"m_CachedPtr")); }
    private ulong TransformOf(ulong componentNative)
    { var owner=Ptr(componentNative+0x20);return Ptr(Ptr(owner+0x20)+8); }
    private bool Active(ulong transform)
    {
        var owner=Ptr(transform+0x20);var value=Read(owner+0x47,1)[0];
        if(value is not (0 or 1))Fail("Unity activeInHierarchy cache not initialized/stable");return value==1;
    }
    private Matrix4x4 World(ulong transform)
    {
        var hierarchy=Ptr(transform+0x28);var index=Int(transform+0x30);
        if(index is <0 or >16383)Fail("Transform index outside bounded profile");
        // GetPosition helper and GetLocalPosition share this exact 48-byte TRS array.
        if(BitConverter.ToUInt64(Read(hierarchy,8))!=0)Fail("Transform jobs pending; passive reader will not synchronize");
        var trs=Ptr(hierarchy+0x18);var parents=Ptr(hierarchy+0x20);var seen=new HashSet<int>();var matrix=Matrix4x4.Identity;
        while(index>=0)
        {
            if(index>16383||seen.Count>=64||!seen.Add(index))Fail("Transform parent cycle/bounds");
            var data=Read(trs+(ulong)index*48,48);var f=Enumerable.Range(0,12).Select(i=>BitConverter.ToSingle(data,i*4)).ToArray();
            if(f.Any(x=>!float.IsFinite(x)))Fail("Nonfinite transform");
            var q=new Quaternion(f[4],f[5],f[6],f[7]);if(Math.Abs(q.LengthSquared()-1)>0.001)Fail("Invalid transform quaternion");
            matrix*=Matrix4x4.CreateScale(f[8],f[9],f[10])*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(f[0],f[1],f[2]);
            index=Int(parents+(ulong)index*4);
        }
        if(Math.Abs(matrix.M12)+Math.Abs(matrix.M21)+Math.Abs(matrix.M13)+Math.Abs(matrix.M23)>0.001
            ||matrix.M11<=0||matrix.M22<=0)Fail("Rotated/reflected UI plane unsupported");
        return matrix;
    }
    private ArenaUiRect Rect(ulong nativeTransform,string label="rect")
    {
        if(Read(nativeTransform+0xf1,1)[0]!=0)Fail("RectTransform cached rect dirty; no native getter invocation");
        var data=Read(nativeTransform+0xb8,16);var f=Enumerable.Range(0,4).Select(i=>BitConverter.ToSingle(data,i*4)).ToArray();
        if(f.Any(x=>!float.IsFinite(x))||f[2]<=0||f[3]<=0)Fail(label+": invalid cached RectTransform rect: "+string.Join(',',f));
        var matrix=World(nativeTransform);var a=Vector3.Transform(new(f[0],f[1],0),matrix);var b=Vector3.Transform(new(f[0]+f[2],f[1]+f[3],0),matrix);
        return new(a.X,a.Y,b.X-a.X,b.Y-a.Y);
    }
    internal ArenaDeckRowGeometryMemory Row(ulong tile,int visualIndex)
    {
        var graphic=Reference(tile,"_raycastImage");ArenaMonoRuntime.Require(graphic,"Deck row raycast image");Assembly(graphic,"UnityEngine.UI");
        var rect=Reference(graphic,"m_RectTransform");ArenaMonoRuntime.Require(rect,"Graphic cached RectTransform");
        var native=TransformOf(Ptr(runtime.Address(tile,"m_CachedPtr")));return new(Rect(native,"Row root"),visualIndex,Active(native),"tile native Component.transform cached rect + audited TRS parent chain");
    }
    internal ArenaDeckHolderGeometryMemory Holder(ulong holder,ulong firstTile)
    {
        var scroll=ScrollRect(holder);Assembly(scroll,"UnityEngine.UI");
        var viewport=Reference(scroll,"m_ViewRect");if(viewport==0)viewport=Reference(scroll,"m_Viewport");
        ArenaMonoRuntime.Require(viewport,"ScrollRect cached viewport");
        var content=ArenaMonoRuntime.Require(Reference(scroll,"m_Content"),"ScrollRect content");
        var parent=ArenaMonoRuntime.Require(Reference(holder,"_cardParent"),"Holder card parent");
        var nativeContent=Native(content,"UnityEngine.CoreModule");
        if(Native(parent,"UnityEngine.CoreModule")!=nativeContent)Fail("Holder parent is not ScrollRect content");
        var graphic=ArenaMonoRuntime.Require(Reference(firstTile,"_raycastImage"),"Row graphic");Assembly(graphic,"UnityEngine.UI");
        var canvas=ArenaMonoRuntime.Require(Reference(graphic,"m_Canvas"),"Graphic cached canvas");
        var nativeCanvas=Native(canvas,"UnityEngine.UIModule");var seen=new HashSet<ulong>();
        while(true)
        {
            if(seen.Count>=32||!seen.Add(nativeCanvas))Fail("Canvas parent cycle/bounds");
            var bytes=Read(nativeCanvas+0x238,8);var parentCanvas=BitConverter.ToUInt64(bytes);if(parentCanvas==0)break;
            RemoteMemory.Validate(parentCanvas,1);nativeCanvas=parentCanvas;
        }
        var mode=Int(nativeCanvas+0x30);var scale=Float(nativeCanvas+0x244);
        var canvasTransform=TransformOf(nativeCanvas);var nativeViewport=Native(viewport,"UnityEngine.CoreModule");
        var canvasWorld=Rect(canvasTransform,"Canvas");
        if(mode!=1||!float.IsFinite(scale)||scale<=0)Fail("Only audited orthographic screen-space camera canvas is supported");
        var cameraId=Int(nativeCanvas+0x34);var camera=Camera(cameraId);
        if(Read(camera+0x513,1)[0]!=1)Fail("Canvas camera is not orthographic");
        var viewportData=Read(camera+0x41c,16);var normalized=Enumerable.Range(0,4).Select(i=>BitConverter.ToSingle(viewportData,i*4)).ToArray();
        if(!normalized.SequenceEqual(new float[]{0,0,1,1}))Fail("Non-full-client camera viewport unsupported");
        var ortho=Float(camera+0x450);var cameraWorld=World(TransformOf(camera));
        if(Math.Abs(2*ortho-canvasWorld.Height)>0.001||Math.Abs(cameraWorld.M41-(canvasWorld.X+canvasWorld.Width/2))>0.001
            ||Math.Abs(cameraWorld.M42-(canvasWorld.Y+canvasWorld.Height/2))>0.001)Fail("Root canvas does not agree with full orthographic camera frustum");
        return new(Rect(nativeViewport,"Viewport"),Rect(nativeContent,"Content"),canvasWorld,mode,scale,Active(nativeViewport),
            Read(runtime.Address(scroll,"m_Dragging"),1)[0]!=0||Read(runtime.Address(scroll,"m_Scrolling"),1)[0]!=0,
            "ScrollRect.m_ViewRect/m_Content + root Graphic.m_Canvas; cached native RectTransform/TRS");
    }
    private ulong Camera(int instanceId)
    {
        if(instanceId==0||unityPlayerBase==0)Fail("Canvas camera ID/module unavailable");
        if(cameras.TryGetValue(instanceId,out var cached)&&Int(cached+8)==instanceId)return cached;
        // Passive warm PPtr lookup: current getter's cache table (0x107390), never its cold resolver.
        var cache=Ptr(unityPlayerBase+0x2201168);var table=Ptr(cache);var mask=Int(cache+8);
        if(mask<8||mask>16_777_208||(mask&7)!=0||((mask+8)&(mask+7))!=0)Fail("Invalid bounded native instance-cache mask");
        var hash=unchecked((uint)instanceId*0x9e3779b1u);var signature=hash&0xfffffffcu;
        var slot=hash&(uint)mask;uint step=8;ulong found=0;
        for(var probe=0;probe<256;probe++)
        {
            var entry=Read(table+3*slot,24);var key=BitConverter.ToUInt32(entry);
            if(key==signature&&BitConverter.ToInt32(entry,8)==instanceId){found=BitConverter.ToUInt64(entry,16);break;}
            if(key==uint.MaxValue)break;
            slot=(slot+step)&(uint)mask;step+=8;
        }
        if(found==0||Ptr(cache)!=table||Int(cache+8)!=mask)Fail("Canvas camera absent/changed in warm native cache");
        RemoteMemory.Validate(found,1);if(Int(found+8)!=instanceId)Fail("Camera native instance ID mismatch");
        cameras[instanceId]=found;return found;
    }
    internal void Check()
    {
        foreach(var check in managedChecks)check();
        foreach(var (address,bytes) in observed)if(!memory.Read(address,bytes.Length).SequenceEqual(bytes))Fail("UI native transform/state changed during read");
    }
    private ulong[] References(MonoListState state)
    {
        var values=runtime.References(state);
        managedChecks.Add(()=>{runtime.CheckList(state);if(!runtime.References(state).SequenceEqual(values))Fail("ScrollRect call targets changed during geometry read");});
        return values;
    }
    private ulong ScrollRect(ulong holder)
    {
        var direct=Reference(holder,"ScrollRect");if(direct!=0)return direct;
        var scrollbar=ArenaMonoRuntime.Require(Reference(holder,"_scrollbar"),"Holder scrollbar");Assembly(scrollbar,"UnityEngine.UI");
        var change=ArenaMonoRuntime.Require(Reference(scrollbar,"m_OnValueChanged"),"Scrollbar change event");
        var persistent=ArenaMonoRuntime.Require(Reference(change,"m_PersistentCalls"),"Scrollbar persistent call group");
        var list=runtime.ListState(Reference(persistent,"m_Calls"),32);var candidates=new List<ulong>();
        foreach(var call in References(list))
        {
            var method=Reference(call,"m_MethodName");var target=Reference(call,"m_Target");
            if(target!=0&&method!=0&&memory.ManagedString(method,128)=="SetVerticalNormalizedPosition"
                &&runtime.ClassName(runtime.ObjectClass(target))=="ScrollRect"&&Int(runtime.Address(call,"m_CallState"))!=0)candidates.Add(target);
        }
        runtime.CheckList(list);
        var calls=ArenaMonoRuntime.Require(Reference(change,"m_Calls"),"Scrollbar invokable calls");
        var runtimeList=runtime.ListState(Reference(calls,"m_RuntimeCalls"),32);
        foreach(var call in References(runtimeList))
        {
            if(runtime.ClassName(runtime.ObjectClass(call))!="InvokableCall`1")continue;
            var action=Reference(call,"Delegate");if(action==0)continue;
            var target=Reference(action,"m_target");
            if(target!=0&&runtime.ClassName(runtime.ObjectClass(target))=="ScrollRect"
                &&Reference(target,"m_VerticalScrollbar")==scrollbar)candidates.Add(target);
        }
        runtime.CheckList(runtimeList);
        if(candidates.Distinct().Count()!=1)Fail("Scrollbar has no unique enabled ScrollRect vertical target");
        return candidates[0];
    }
    private static void Fail(string reason)=>throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure,reason);
}
