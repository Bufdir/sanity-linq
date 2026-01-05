// Reference: https://msdn.microsoft.com/en-us/library/bb546158.aspx

namespace Sanity.Linq.Internal;

internal static class Evaluator
{
    /// <summary>
    /// Performs evaluation and replacement of independent subtrees
    /// </summary>
    /// <param name="expression">The root of the expression tree.</param>
    /// <param name="fnCanBeEvaluated">A function that decides whether a given expression node can be part of the local function.</param>
    /// <returns>A new tree with sub-trees evaluated and replaced.</returns>
    public static Expression PartialEval(Expression expression, Func<Expression, bool> fnCanBeEvaluated)
    {
        return new SubtreeEvaluator(new Nominator(fnCanBeEvaluated).Nominate(expression)).Eval(expression);
    }

    /// <summary>
    /// Performs evaluation & replacement of independent sub-trees
    /// </summary>
    /// <param name="expression">The root of the expression tree.</param>
    /// <returns>A new tree with subtrees evaluated and replaced.</returns>
    public static Expression PartialEval(Expression expression)
    {
        return PartialEval(expression, CanBeEvaluatedLocally);
    }

    private static bool CanBeEvaluatedLocally(Expression expression)
    {
        if (expression.NodeType == ExpressionType.Parameter)
        {
            return false;
        }

        if (expression is ConstantExpression)
        {
            return true;
        }

        if (expression is MethodCallExpression m)
        {
            var declaringType = m.Method.DeclaringType;
            if (declaringType == typeof(Queryable) ||
                (declaringType != null && (declaringType.Name == "SanityDocumentSetExtensions" ||
                                           typeof(SanityDocumentSet).IsAssignableFrom(declaringType))))
            {
                return false;
            }
        }

        // Check if expression or its children depend on a ParameterExpression
        return !DependsOnParameter(expression);
    }

    private static bool DependsOnParameter(Expression expression)
    {
        var visitor = new ParameterFinder();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private class ParameterFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node?.NodeType == ExpressionType.Parameter)
            {
                Found = true;
            }

            return Found ? node : base.Visit(node);
        }
    }

    /// <summary>
    /// Performs bottom-up analysis to determine which nodes can possibly
    /// be part of an evaluated sub-tree.
    /// </summary>
    private class Nominator : ExpressionVisitor
    {
        private readonly Func<Expression, bool> _fnCanBeEvaluated;
        private HashSet<Expression> _candidates = [];
        private bool _cannotBeEvaluated;

        internal Nominator(Func<Expression, bool> fnCanBeEvaluated)
        {
            _fnCanBeEvaluated = fnCanBeEvaluated;
        }

        public override Expression? Visit(Expression? expression)
        {
            if (expression == null)
            {
                return expression;
            }

            var saveCannotBeEvaluated = _cannotBeEvaluated;
            _cannotBeEvaluated = false;
            base.Visit(expression);
            if (!_cannotBeEvaluated)
            {
                if (_fnCanBeEvaluated(expression))
                {
                    _candidates.Add(expression);
                }
                else
                {
                    _cannotBeEvaluated = true;
                }
            }
            _cannotBeEvaluated |= saveCannotBeEvaluated;
            return expression;
        }

        internal HashSet<Expression> Nominate(Expression expression)
        {
            _candidates = [];
            Visit(expression);
            return _candidates;
        }
    }

    /// <summary>
    /// Evaluates & replaces sub-trees when first candidate is reached (top-down)
    /// </summary>
    private class SubtreeEvaluator : ExpressionVisitor
    {
        private readonly HashSet<Expression> _candidates;

        internal SubtreeEvaluator(HashSet<Expression> candidates)
        {
            _candidates = candidates;
        }

        public override Expression? Visit(Expression? exp)
        {
            if (exp == null)
            {
                return exp;
            }
            if (_candidates.Contains(exp))
            {
                return Evaluate(exp);
            }
            return base.Visit(exp);
        }

        internal Expression Eval(Expression exp)
        {
            return Visit(exp)!;
        }

        private static Expression Evaluate(Expression e)
        {
            if (e.NodeType == ExpressionType.Constant)
            {
                return e;
            }
            try
            {
                var lambda = Expression.Lambda(e);
                var fn = lambda.Compile();
                var result = fn.DynamicInvoke(null);
                return Expression.Constant(result, e.Type);
            }
            catch
            {
                return e;
            }
        }
    }
}