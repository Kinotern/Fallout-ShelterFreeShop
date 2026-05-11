using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static string targetDll = @"O:\SteamLibrary\steamapps\common\Fallout Shelter\FalloutShelter_Data\Managed\Assembly-CSharp.dll";

    static void Main()
    {
        Console.WriteLine("=== Fallout Shelter IAP Patch v5 ===");

        string backup = targetDll + ".backup";
        if (!File.Exists(backup))
            File.Copy(targetDll, backup);

        string tmp = Path.GetTempFileName() + ".dll";
        File.Copy(targetDll, tmp, true);

        int total = 0;
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(targetDll));
            using (var asm = AssemblyDefinition.ReadAssembly(tmp,
                new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadWrite = true
                }))
            {
                total += PatchOnAwake(asm);
                total += PatchCurrentController(asm);
                total += PatchAllIsOverlayEnabled(asm);
                total += PatchFetchSellableItems(asm);

                if (total > 0)
                {
                    asm.Write(new WriterParameters { WriteSymbols = false });
                    Console.WriteLine($"\n=== {total} patches applied ===");
                }
            }

            if (total > 0)
            {
                byte[] data = File.ReadAllBytes(tmp);
                bool ok = false;
                for (int i = 0; i < 5; i++)
                {
                    try { File.WriteAllBytes(targetDll, data); ok = true; break; }
                    catch (IOException)
                    { Console.Write("."); System.Threading.Thread.Sleep(800); }
                }
                Console.WriteLine(ok ? "\nDLL patched. Unlimited IAP enabled."
                    : "\nTarget locked. Close Steam/game and re-run.");
            }
            else
                Console.WriteLine("Nothing to patch.");
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    static int PatchOnAwake(AssemblyDefinition asm)
    {
        var m = FindMethod(asm.MainModule.GetType("PurchaseManager"), "OnAwake");
        if (m?.Body == null) return 0;
        foreach (var i in m.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Ldc_I4_0 && i.Next?.OpCode == OpCodes.Call
                && (i.Next.Operand as MethodReference)?.Name == "set_UseDebugShop")
            {
                m.Body.GetILProcessor().Replace(i, Instruction.Create(OpCodes.Ldc_I4_1));
                Console.WriteLine("  [OnAwake] UseDebugShop=true");
                return 1;
            }
        }
        return 0;
    }

    static int PatchCurrentController(AssemblyDefinition asm)
    {
        var pm = asm.MainModule.GetType("PurchaseManager");
        var m = FindMethod(pm, "get_CurrentPurchaseController");
        if (m?.Body == null) return 0;

        var fld = FindField(pm, "m_debugPurchaseController");
        if (fld == null) return 0;

        m.Body.Instructions.Clear();
        m.Body.Variables.Clear();
        var il = m.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, fld));
        il.Append(il.Create(OpCodes.Ret));
        Console.WriteLine("  [get_CurrentPurchaseController] -> editor");
        return 1;
    }

    static int PatchAllIsOverlayEnabled(AssemblyDefinition asm)
    {
        int n = 0;
        foreach (var t in asm.MainModule.Types)
            n += PatchIsOverlayInType(t);
        Console.WriteLine($"  [IsOverlayEnabled] {n} calls -> true");
        return n;
    }

    static int PatchIsOverlayInType(TypeDefinition type)
    {
        int n = 0;
        if (type.HasNestedTypes)
            foreach (var nt in type.NestedTypes) n += PatchIsOverlayInType(nt);
        if (type.HasMethods)
            foreach (var m in type.Methods) n += PatchIsOverlayInMethod(m);
        return n;
    }

    static int PatchIsOverlayInMethod(MethodDefinition method)
    {
        if (method.Body == null) return 0;
        int n = 0;
        var toReplace = new List<Instruction>();
        foreach (var i in method.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Call)
            {
                var mr = i.Operand as MethodReference;
                if (mr != null && mr.Name == "IsOverlayEnabled"
                    && mr.DeclaringType.Name == "SteamUtils")
                {
                    toReplace.Add(i);
                }
            }
        }
        var il = method.Body.GetILProcessor();
        foreach (var i in toReplace)
        {
            il.Replace(i, il.Create(OpCodes.Ldc_I4_1));
            n++;
        }
        return n;
    }

    static int PatchFetchSellableItems(AssemblyDefinition asm)
    {
        var m = FindMethod(asm.MainModule.GetType("PurchaseControllerSteam"),
            "FetchSellableItemsFromSteam");
        if (m?.Body == null) return 0;

        var il = m.Body.GetILProcessor();
        int n = 0;
        var reps = new List<Instruction>();
        foreach (var i in m.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Callvirt)
            {
                var mr = i.Operand as MethodReference;
                if (mr != null && mr.Name == "get_UseDebugShop")
                {
                    reps.Add(i);
                }
            }
        }
        foreach (var i in reps)
        {
            il.Replace(i, il.Create(OpCodes.Ldc_I4_1));
            n++;
        }
        if (n > 0)
            Console.WriteLine($"  [FetchSellableItems] {n} UseDebugShop checks -> true");
        return n;
    }

    static MethodDefinition FindMethod(TypeDefinition t, string name)
    {
        foreach (var m in t.Methods) if (m.Name == name) return m;
        return null;
    }

    static FieldDefinition FindField(TypeDefinition t, string name)
    {
        foreach (var f in t.Fields) if (f.Name == name) return f;
        return null;
    }
}
