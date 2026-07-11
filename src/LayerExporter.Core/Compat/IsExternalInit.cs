#if NETFRAMEWORK
// C# 9 record/init 접근자를 .NET Framework 4.8에서 컴파일하기 위한 폴리필
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
