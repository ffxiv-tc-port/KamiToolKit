using System;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Memory;

namespace KamiToolKit.Classes;

internal static class NativeMemoryHelper {
    public static unsafe T* UiAlloc<T>(int elementCount, ulong alignment = 8) where T : unmanaged
        => UiAlloc<T>((uint)elementCount, alignment);

    public static unsafe T* UiAlloc<T>(uint elementCount = 1, ulong alignment = 8) where T : unmanaged {
        var allocSize = (ulong)sizeof(T) * elementCount;

        // 判空必須排在 Memset 之前：先 Memset 再判空的話，
        // 配置失敗時會先對位址 0 寫入 allocSize 個位元組 —— 那是 try/catch 攔不到的
        // AccessViolationException，下面那個 throw 永遠走不到。
        var uiSpace = IMemorySpace.GetUISpace();
        if (uiSpace is null) {
            throw new Exception($"UI memory space is unavailable, cannot allocate memory for {typeof(T)}");
        }

        var memory = (T*)uiSpace->Malloc(allocSize, alignment);

        if (memory is null) {
            throw new Exception($"Unable to allocate memory for {typeof(T)}");
        }

        IMemorySpace.Memset(memory, 0, allocSize);

        return memory;
    }

    public static unsafe void UiFree<T>(T* memory) where T : unmanaged
        => IMemorySpace.Free(memory);

    public static unsafe void UiFree<T>(T* memory, uint elementCount) where T : unmanaged
        => IMemorySpace.Free(memory, (ulong)sizeof(T) * elementCount);

    public static unsafe T* Create<T>() where T : unmanaged, ICreatable {
        // 同上：GetUISpace() 回 null 時 ->Create<T>() 是對位址 0 的呼叫，不是可攔截的例外。
        var uiSpace = IMemorySpace.GetUISpace();
        if (uiSpace is null) {
            throw new Exception($"UI memory space is unavailable, cannot create {typeof(T)}");
        }

        var memory = uiSpace->Create<T>();

        if (memory is null) {
            throw new Exception($"Unable to allocate memory for {typeof(T)}");
        }

        return memory;
    }

    public static unsafe nint Malloc(ulong size, ulong alignment = 8)
        => (nint)IMemorySpace.GetUISpace()->Malloc(size, alignment);

    public static unsafe void Free(void* memory, ulong size)
        => IMemorySpace.Free(memory, size);

    public static unsafe void ResizeArray<T>(ref T* array, int oldSize, uint newSize) where T : unmanaged
        => ResizeArray(ref array, oldSize, (int)newSize);

    public static unsafe void ResizeArray<T>(ref T* array, uint oldSize, uint newSize) where T : unmanaged
        => ResizeArray(ref array, (int)oldSize, (int)newSize);

    public static unsafe void ResizeArray<T>(ref T* array, uint oldSize, int newSize) where T : unmanaged
        => ResizeArray(ref array, (int)oldSize, newSize);

    public static unsafe void ResizeArray<T>(ref T* array, int oldSize, int newSize) where T : unmanaged {
        var newBuffer = UiAlloc<T>((uint)newSize);

        Copy(array, newBuffer, oldSize);

        if (array is not null) {
            UiFree(array, (uint)oldSize);
        }

        array = newBuffer;
    }

    public static unsafe void Copy<T>(T* oldBuffer, T* newBuffer, int count) where T : unmanaged
        => Copy(oldBuffer, newBuffer, (uint)count);

    public static unsafe void Copy<T>(T* oldBuffer, T* newBuffer, uint count) where T : unmanaged
        => NativeMemory.Copy(oldBuffer, newBuffer, (nuint)(sizeof(T) * count));

    public static unsafe void MemCopy<T>(T* oldBuffer, T* newBuffer, uint byteCount) where T : unmanaged
        => NativeMemory.Copy(oldBuffer, newBuffer, byteCount);
}
