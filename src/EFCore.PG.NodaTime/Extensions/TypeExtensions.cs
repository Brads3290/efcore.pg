using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Npgsql.EntityFrameworkCore.PostgreSQL;

internal static class TypeExtensions
{
    internal static bool IsGenericList(this Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    internal static bool IsArrayOrGenericList(this Type type)
        => type.IsArray || type.IsGenericList();

    internal static bool TryGetElementType(this Type type, out Type? elementType)
    {
        elementType = type.IsArray
            ? type.GetElementType()
            : type.IsGenericList()
                ? type.GetGenericArguments()[0]
                : null;
        return elementType is not null;
    }
}