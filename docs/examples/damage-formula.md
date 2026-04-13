# 示例：游戏伤害公式

典型的游戏伤害计算公式——类似 Noita 中法术修正符的链式叠加——演示多变量、多运算符嵌套、隐式乘法的使用。

## 场景

```
最终伤害 = (基础攻击力 × 技能倍率 + 额外伤害) × (1 + 暴击伤害加成) × 防御减免
```

公式表达式：`([atk] * [skill] + [bonus]) * (1 + [crit]) * [def_factor]`

## 操作符枚举

```csharp
public enum DamageOp : byte
{
    Const, Add, Sub, Mul, Div,
    LParen, RParen, Return,
}
```

## 定义体

```csharp
public readonly struct DamageDef : IFluxJITDefinition<float, DamageOp>
{
    public DamageOp GetReturnOp() => DamageOp.Return;

    public int GetArity(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Add => 2, DamageOp.Sub => 2,
        DamageOp.Mul => 2, DamageOp.Div => 2,
        _ => 0,
    };

    public OpType GetKind(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Const  => OpType.Immediate,
        DamageOp.Return => OpType.Return,
        _               => OpType.Instruction,
    };

    public int GetPrecedence(DamageOp op) => op switch
    {
        DamageOp.Add => 1, DamageOp.Sub => 1,
        DamageOp.Mul => 2, DamageOp.Div => 2,
        _            => 0,
    };

    public OpPair<DamageOp> GetPair(DamageOp op) => op switch
    {
        DamageOp.LParen => new OpPair<DamageOp> { PairRole = Pair.Left },
        DamageOp.RParen => new OpPair<DamageOp>
        {
            PairRole   = Pair.Right,
            TargetLeft = DamageOp.LParen,
        },
        _ => new OpPair<DamageOp> { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(DamageOp op) => Associativity.Left;

    public DamageOp ResolveToken(DamageOp op, TokenContext ctx) => op;

    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return ((DamageOp)op) switch
        {
            DamageOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            DamageOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            DamageOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            DamageOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            _ => 0f,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((DamageOp)op) switch
        {
            DamageOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            _ => Expression.Constant(0f),
        };
    }
}
```

## 使用

```csharp
var config = new LexerConfig<float, DamageOp>
{
    LiteralOper    = DamageOp.Const,
    LiteralParser  = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators      = { new("+", DamageOp.Add), new("-", DamageOp.Sub),
                       new("*", DamageOp.Mul), new("/", DamageOp.Div) },
    Brackets       = { new("(", ")", DamageOp.LParen, DamageOp.RParen) },
    VariablePatterns = { new("[", "]") },
    ImplicitOperators = { DamageOp.Mul },
};

var def    = new DamageDef();
var runner = new FluxAssembler<float, DamageOp, DamageDef>(def);
var lexer  = new FluxLexer<float, DamageOp>(config);

// 编译一次，反复求值
var formula = runner.Compile(lexer.Lex(
    "([atk] * [skill] + [bonus]) * (1 + [crit]) * [def_factor]"));

// 暴击场景
float critDmg = runner.Instantiate(formula)
    .Set("atk", 250f)
    .Set("skill", 1.8f)
    .Set("bonus", 50f)
    .Set("crit", 1.5f)      // +150% 暴击伤害
    .Set("def_factor", 0.7f) // 敌人减免后剩 70%
    .Run();
// (250 * 1.8 + 50) * 2.5 * 0.7 = 875

// 非暴击场景（crit = 0）
float normalDmg = runner.Instantiate(formula)
    .Set("atk", 250f)
    .Set("skill", 1.8f)
    .Set("bonus", 50f)
    .Set("crit", 0f)
    .Set("def_factor", 0.7f)
    .Run();
// (250 * 1.8 + 50) * 1.0 * 0.7 = 350
```

## 要点

- **隐式乘法**：`(1 + [crit])` 后的 `*` 是显式的，但 `2[atk]` 等写法可通过 `ImplicitOperators` 启用
- **变量重用**：同一 Formula 可多次 `Instantiate` 后设不同变量值，适合批量计算
- **性能**：公式编译一次（~100 ns + ~500 B），后续每次求值 ~20 ns（解释器）或 ~3 ns（JIT）
