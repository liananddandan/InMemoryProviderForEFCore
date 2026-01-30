using System.Text.Json;

namespace CustomMemoryEFProvider.Core.Helpers;

/// <summary>
/// 通用对象深拷贝工具（适配展示项目，优先用Json序列化，兼容大部分实体）
/// </summary>
public static class ObjectCloner
{
    /// <summary>
    /// 创建对象的深拷贝（支持简单实体，无循环引用）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>独立的深拷贝对象</returns>
    public static T DeepClone<T>(T source) where T : class
    {
        if (source == null) return null!;

        // 方案1：Json序列化（简单通用，适合展示项目）
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json)!;

        // 方案2：反射（性能更好，适合复杂实体，可选）
        // var clone = Activator.CreateInstance<T>();
        // foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        // {
        //     if (prop.CanRead && prop.CanWrite)
        //     {
        //         var value = prop.GetValue(source);
        //         prop.SetValue(clone, value);
        //     }
        // }
        // return clone;
    }
}