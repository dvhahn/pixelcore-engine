using System.Linq;
using System.Reflection;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor;

public static class EditorReflection
{
    public static readonly MethodInfo AddComponentOpen = typeof(Entity)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m.Name == nameof(Entity.AddComponent) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
}
