using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TownOfHost.Modules;

namespace TownOfHost.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public abstract class InitializerAttribute<T> : Attribute
{
    /// <summary>全初期化メソッド</summary>
    private static MethodInfo[] allInitializers = null;
    private static LogHandler logger = Logger.Handler(nameof(InitializerAttribute<T>));

    public InitializerAttribute() : this(InitializePriority.Normal) { }
    public InitializerAttribute(InitializePriority priority)
    {
        this.priority = priority;
    }

    private readonly InitializePriority priority = InitializePriority.Normal;
    /// <summary>初期化時に呼び出されるメソッド</summary>
    private MethodInfo targetMethod;

    private static void FindInitializers()
    {
        var initializers = new HashSet<InitializerAttribute<T>>(32);

        // TownOfHost.dll内の
        var assembly = Assembly.GetExecutingAssembly();
        // 全クラス内の
        var types = assembly.GetTypes();
        foreach (var type in types)
        {
            // 全メソッドについて
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                // InitializerAttributeを取得
                var attribute = method.GetCustomAttribute<InitializerAttribute<T>>();
                if (attribute != null)
                {
                    // 取得できたら登録
                    attribute.targetMethod = method;
                    initializers.Add(attribute);
                }
            }
        }
        // 見つかった初期化メソッドをpriority順に並べ替えて配列に変換
        allInitializers = initializers.OrderBy(initializer => initializer.priority).Select(initializer => initializer.targetMethod).ToArray();
    }
    public static void InitializeAll(bool Log = false)
    {
        // 初回の初期化時に初期化メソッドを探す
        if (allInitializers == null)
        {
            FindInitializers();
        }
        foreach (var initializer in allInitializers)
        {
            if (/*Log && */initializer.Name is not "Load") logger.Info($"初期化: {initializer.DeclaringType.Name}.{initializer.Name}");
            initializer.Invoke(null, null);
        }
    }

    /// <summary>
    /// 起動時ロード画面から呼ぶための、進行状況を取得しながら初期化するイテレータ。
    /// 1回のMoveNext()で初期化メソッドを1つだけ実行し、(処理した数, 全体数)を返す。
    /// 呼び出し側でコルーチン(1フレームに数回MoveNext)にすることで、
    /// 数百件の初期化処理を1フレームで一気に実行してフリーズして見えるのを防げる。
    /// </summary>
    public static System.Collections.Generic.IEnumerable<(int done, int total)> InitializeAllStepwise()
    {
        if (allInitializers == null)
        {
            FindInitializers();
        }
        var total = allInitializers.Length;
        for (var i = 0; i < total; i++)
        {
            var initializer = allInitializers[i];
            if (initializer.Name is not "Load") logger.Info($"初期化: {initializer.DeclaringType.Name}.{initializer.Name}");
            initializer.Invoke(null, null);
            yield return (i + 1, total);
        }
    }
}

public enum InitializePriority
{
    /// <summary>一番最初に実行される</summary>
    VeryHigh,
    /// <summary>既定値より前に実行される</summary>
    High,
    /// <summary>既定値</summary>
    Normal,
    /// <summary>既定値より後に実行される</summary>
    Low,
    /// <summary>一番最後に実行される</summary>
    VeryLow,
}
