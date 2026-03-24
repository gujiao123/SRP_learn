using System.Runtime.InteropServices;

// me14: int 转 float 的内存重解释扩展方法
// 普通 (float)mask 会做数值转换，改变比特位模式
// 这里用 Union Struct（共享内存）实现原样传递比特，不做数值转换
// GPU 端用 asuint() 读回来，就能还原原始的位掩码
public static class ReinterpretExtensions
{
    // Union Struct：两个字段共享同一块 4 字节内存
    [StructLayout(LayoutKind.Explicit)]
    struct IntFloat
    {
        [FieldOffset(0)] public int intValue;     // ← 同一块内存
        [FieldOffset(0)] public float floatValue; // ← 用不同类型解释
    }

    // 用法：light.renderingLayerMask.ReinterpretAsFloat()
    public static float ReinterpretAsFloat(this int value)
    {
        IntFloat converter = default;
        converter.intValue = value;
        return converter.floatValue; // 比特位完全相同，只是换了解释方式
    }
}

//?什么意思 