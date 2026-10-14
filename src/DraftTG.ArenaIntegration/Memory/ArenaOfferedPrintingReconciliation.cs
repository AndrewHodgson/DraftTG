namespace DraftTG.ArenaIntegration.Memory;

/// <summary>Preserves pod occurrence counts and visual order. Different pod printings never compete arbitrarily.</summary>
public static class ArenaOfferedPrintingReconciliation
{
    public static ArenaOfferedCardIdentity[] Match(IReadOnlyList<uint> pod, IReadOnlyList<uint> represented,
        IReadOnlyList<uint> visible, Func<uint,uint,ArenaOfferedCardIdentity> proof)
    {
        if(pod.Count!=visible.Count||represented.Count!=visible.Count)throw new InvalidDataException("Printing occurrence counts disagree");
        var remaining=pod.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        var result=new ArenaOfferedCardIdentity[visible.Count];
        // Reserve exact matches first; a preferred-art occurrence cannot steal another occurrence's exact identity.
        var exact=new bool[visible.Count];
        for(var i=0;i<visible.Count;i++)
            if(remaining.GetValueOrDefault(visible[i])>0) {exact[i]=true;remaining[visible[i]]--;}
        for(var i=0;i<visible.Count;i++)
        {
            var representedProof=proof(represented[i],visible[i]);
            if(!representedProof.Accepted)throw new InvalidDataException("Represented/visible printing: "+representedProof.Reason);
            var candidates=exact[i]?new[]{proof(visible[i],visible[i])}:remaining.Where(x=>x.Value>0)
                .Select(x=>proof(x.Key,visible[i])).Where(x=>x.Accepted).ToArray();
            if(candidates.Length!=1||!candidates[0].Accepted)throw new InvalidDataException("Pod/visible printing equivalence absent or ambiguous");
            result[i]=candidates[0];
            if(!exact[i])remaining[result[i].PodGrpId]--;
        }
        if(remaining.Values.Any(n=>n!=0))throw new InvalidDataException("Unmatched pod printing occurrences");
        return result;
    }
}
