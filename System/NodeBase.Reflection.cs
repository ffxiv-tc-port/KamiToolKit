using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KamiToolKit.Classes;

namespace KamiToolKit.System;

public abstract partial class NodeBase {
    // 成員清單只依執行期型別而定，每個型別算一次；走訪可能從任何執行緒進來（例：插件在背景執行緒 Dispose）。
    // 每次走訪仍逐一 GetValue，所以讀到的永遠是當下的欄位值。
    private static readonly ConcurrentDictionary<Type, (MemberInfo[] Nodes, MemberInfo[] Enumerables)> ChildMemberCache = new();

    private void VisitChildren(Action<NodeBase?> visitAction) {
        try {
            foreach (var child in GetChildren(this)) {
                DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
                    visitAction(child);
                });
            }
        }
        catch (Exception e) {
            Log.Exception(e);
        }
    }

    private IEnumerable<NodeBase?> GetChildren(NodeBase parent) {
        var (nodeMembers, enumerableMembers) = ChildMemberCache.GetOrAdd(parent.GetType(), static type => (GetNodeMembers(type), GetEnumerableMemberInfo(type)));

        foreach (var member in nodeMembers) {
            if (GetNode(member, parent) is { } node)
                yield return node;
        }

        foreach (var enumerableMember in enumerableMembers) {
            if (GetEnumerable(enumerableMember, parent) is { } enumerable) {
                foreach (var child in enumerable) {
                    yield return child;
                }
            }
        }
    }

    private static bool IsNodeMember(MemberInfo member)
        => typeof(NodeBase).IsAssignableFrom(GetMemberType(member));

    private static MemberInfo[] GetNodeMembers(Type type)
        => GetMemberInfo(type).Where(IsNodeMember).Where(IsMemberSingleIndexable).ToArray();

    private static IEnumerable<MemberInfo> GetMemberInfo(Type type)
        => type.GetMembers(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(member => member.MemberType is MemberTypes.Field or MemberTypes.Property);

    private static NodeBase? GetNode(MemberInfo member, NodeBase node) => member.MemberType switch {
        MemberTypes.Field => (member as FieldInfo)?.GetValue(node) as NodeBase,
        MemberTypes.Property => (member as PropertyInfo)?.GetValue(node) as NodeBase,
        _ => null,
    };

    private static MemberInfo[] GetEnumerableMemberInfo(Type type)
        => GetMemberInfo(type).Where(IsMemberEnumerable).Where(IsBaseNodeEnumerable).Where(IsMemberSingleIndexable).ToArray();

    private static bool IsMemberEnumerable(MemberInfo member)
        => GetMemberType(member)?.GetInterfaces().Contains(typeof(IEnumerable)) ?? false;

    private static bool IsBaseNodeEnumerable(MemberInfo member)
        => typeof(NodeBase).IsAssignableFrom(GetMemberType(member)?.GetGenericArguments().FirstOrDefault());

    private static bool IsMemberSingleIndexable(MemberInfo member) {
        if (member is not PropertyInfo property) return true;

        return property.GetIndexParameters().Length is 0;
    }

    private static IEnumerable<NodeBase?>? GetEnumerable(MemberInfo member, NodeBase node) => member.MemberType switch {
        MemberTypes.Field => (member as FieldInfo)?.GetValue(node) as IEnumerable<NodeBase>,
        MemberTypes.Property => (member as PropertyInfo)?.GetValue(node) as IEnumerable<NodeBase>,
        _ => null,
    };

    private static Type? GetMemberType(MemberInfo member) => member.MemberType switch {
        MemberTypes.Field => (member as FieldInfo)?.FieldType,
        MemberTypes.Property => (member as PropertyInfo)?.PropertyType,
        _ => null,
    };
}
