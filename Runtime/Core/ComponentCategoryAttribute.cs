using System;

namespace PixelCore.Runtime.Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ComponentCategoryAttribute : Attribute
{
    public string Category { get; }
    public ComponentCategoryAttribute(string category) => Category = category;
}

public static class ComponentCategories
{
    public const string Basic = "Basic";
    public const string Render = "Render";
    public const string Physics = "Physics";
    public const string Interaction = "Interaction";
    public const string Sound = "Sound and floor";
    public const string Game = "Game";
    public const string Other = "Other";
    public const string Hidden = "Hidden";
}
