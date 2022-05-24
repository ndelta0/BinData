using System.Linq.Expressions;

namespace BinData;

internal record BuildingInfo(List<Expression> Expressions, Type Type, Expression Value, ParameterExpression Stream, ParameterExpression Iterator)
{
    public void Add(Expression expression)
    {
        Expressions.Add(expression);
    }
}
