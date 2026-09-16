using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelCore.Runtime.Core;

public interface ICondition
{
    bool IsSatisfied(Blackboard bb);
}

public enum Comparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }

public class FlagCondition : ICondition
{
    public string Key;
    public bool Expected;

    public FlagCondition(string key, bool expected = true)
    {
        Key = key;
        Expected = expected;
    }

    public bool IsSatisfied(Blackboard bb) => bb.GetBool(Key) == Expected;
}

public class IntCondition : ICondition
{
    public string Key;
    public int Value;
    public Comparison Op;

    public IntCondition(string key, Comparison op, int value)
    {
        Key = key;
        Op = op;
        Value = value;
    }

    public bool IsSatisfied(Blackboard bb)
    {
        int v = bb.GetInt(Key);
        return Op switch
        {
            Comparison.Equal => v == Value,
            Comparison.NotEqual => v != Value,
            Comparison.Greater => v > Value,
            Comparison.GreaterOrEqual => v >= Value,
            Comparison.Less => v < Value,
            Comparison.LessOrEqual => v <= Value,
            _ => false,
        };
    }
}

public class AndCondition : ICondition
{
    public ICondition[] Conditions;
    public AndCondition(params ICondition[] conditions) => Conditions = conditions;
    public bool IsSatisfied(Blackboard bb) => Conditions.All(c => c.IsSatisfied(bb));
}

public class OrCondition : ICondition
{
    public ICondition[] Conditions;
    public OrCondition(params ICondition[] conditions) => Conditions = conditions;
    public bool IsSatisfied(Blackboard bb) => Conditions.Any(c => c.IsSatisfied(bb));
}

public class NotCondition : ICondition
{
    public ICondition Inner;
    public NotCondition(ICondition inner) => Inner = inner;
    public bool IsSatisfied(Blackboard bb) => !Inner.IsSatisfied(bb);
}

public class FuncCondition : ICondition
{
    public Func<Blackboard, bool> Predicate;
    public FuncCondition(Func<Blackboard, bool> predicate) => Predicate = predicate;
    public bool IsSatisfied(Blackboard bb) => Predicate(bb);
}

public static class Conditions
{
    public static ICondition Flag(string key, bool expected = true) => new FlagCondition(key, expected);
    public static ICondition Int(string key, Comparison op, int value) => new IntCondition(key, op, value);
    public static ICondition And(params ICondition[] c) => new AndCondition(c);
    public static ICondition Or(params ICondition[] c) => new OrCondition(c);
    public static ICondition Not(ICondition c) => new NotCondition(c);
    public static ICondition When(Func<Blackboard, bool> p) => new FuncCondition(p);

    public static bool Check(ICondition? c, Blackboard bb) => c == null || c.IsSatisfied(bb);
}
