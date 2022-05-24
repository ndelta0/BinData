using System.Linq.Expressions;

namespace BinData;

internal record BuildingInfo(List<Expression> Expressions, List<ParameterExpression> Variables, Type Type, Expression Value, ParameterExpression Stream, ParameterExpression Iterator, ParameterExpression IteratorEnd)
{
    public void Add(Expression expression)
    {
        Expressions.Add(expression);
    }
}
