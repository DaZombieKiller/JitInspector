# JIT Inspector
A tool for inspecting Mono JIT code in Unity.

![](/README~/Screenshot.png)

# Usage
Open the tool from the `Window -> JIT Inspector View` menu option.

# JIT Configuration
You can configure the JIT using the following class:
```cs
#if ENABLE_MONO
using System.Text;
using System.Runtime.InteropServices;

internal static unsafe class MonoJitConfig
{
    private const string Optimizations = "-float32";

    [DllImport("__Internal")]
    private static extern void mono_jit_parse_options(int argc, byte** argv);

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#else
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
    private static void ParseOptions()
    {
        fixed (byte* arg = Encoding.UTF8.GetBytes("-O=" + Optimizations + "\0"))
        {
            mono_jit_parse_options(1, &arg);
        }
    }
}
#endif
```
The `Optimizations` string is a comma-separated (without spaces) list of optimization passes for the JIT to perform. Prefixing an optimization with a minus (`-`) will disable it.

You may use `all` to enable all optimizations and `-all` to disable all optimizations.

Entries later in the list will override entries before them, meaning `-branch,branch` will result in `branch` being enabled. `-all,branch` will result in *only* `branch` being enabled.

Available optimization passes are:

Name | Description | Enabled By Default
-|-|-
`peephole` | Peephole postpass | Yes
`branch` | Branch optimizations | Yes
`inline` | Inline method calls | Yes
`cfold` | Constant folding | Yes
`consprop` | Constant propagation | Yes
`copyprop` | Copy propagation | Yes
`deadce` | Dead code elimination | Yes
`linears` | Linear scan global reg allocation | Yes
`cmov` | Conditional moves | Yes
`shared` | Emit per-domain code | No<sup>1</sup>
`sched` | Instruction scheduling | No
`intrins` | Intrinsic method implementations | Yes
`tailc` | Tail recursion and tailcalls | No
`loop` | Loop related optimizations | Yes
`fcmov` | Fast x86 FP compares | No
`leaf` | Leaf procedures and optimizations | No
`aot` | Usage of Ahead Of Time compiled code | Yes
`precomp` | Precompile all methods before executing | No<sup>1</sup>
`abcrem` | Array bound checks removal | No
`ssapre` | SSA based Partial Redundancy | No
`exception` | Optimize exception catch blocks | Yes
`ssa` | Use plain SSA form | No
`float32` | Use 32 bit float arithmetic if possible | No<sup>2</sup>
`sse2` | SSE2 instructions on x86 | No
`gsharedvt` | Generic sharing for valuetypes | No<sup>1</sup>
`gshared` | Generic Sharing | Yes
`simd` | Simd intrinsics | Yes
`unsafe` | Remove bound checks and perform other dangerous changes | No<sup>1</sup>
`alias-analysis` | Alias analysis of locals | Yes
`aggressive-inlining` | Aggressive Inlining | No

<sup>1</sup> These optimizations are not enabled by `all` and must be specified manually.

<sup>2</sup> Enabled by default in Mono, disabled by default in Unity.
