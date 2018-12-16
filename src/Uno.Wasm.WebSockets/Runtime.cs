using System.Runtime.CompilerServices;

namespace WebAssembly
{
    internal sealed class Runtime
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string InvokeJS(string str, out int exceptional_result);
    }
}