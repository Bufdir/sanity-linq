// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT License.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT License for more details.

//  You should have received a copy of the MIT License
//  along with this program.

using Sanity.Linq.Internal;
// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal sealed class SanityQueryProvider(Type docType, SanityDataContext context, int maxNestingLevel) : IQueryProvider
{
    public SanityDataContext Context { get; } = context;
    public Type DocType { get; } = docType;
    public int MaxNestingLevel { get; } = maxNestingLevel;

    /// <summary>
    ///     Constructs an <see cref="IQueryable" /> object that can evaluate the query represented by the specified expression
    ///     tree.
    /// </summary>
    /// <param name="expression">
    ///     An <see cref="Expression" /> that represents the LINQ query to be evaluated.
    /// </param>
    /// <returns>
    ///     An <see cref="IQueryable" /> that can evaluate the query represented by the specified expression tree.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the queryable instance cannot be created.
    /// </exception>
    /// <exception cref="TargetInvocationException">
    ///     Thrown when there is an error during the creation of the queryable instance.
    /// </exception>
    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = TypeSystem.GetElementType(expression.Type);
        try
        {
            var instance = Activator.CreateInstance(
                typeof(SanityDocumentSet<>).MakeGenericType(elementType),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                [this, expression],
                null);
            return (IQueryable)(instance ?? throw new InvalidOperationException("Failed to create queryable instance."));
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

    /// <summary>
    ///     Constructs an <see cref="IQueryable{T}" /> object that can evaluate the query represented by the specified
    ///     expression tree.
    /// </summary>
    /// <typeparam name="TElement">
    ///     The type of the elements in the resulting <see cref="IQueryable{T}" />.
    /// </typeparam>
    /// <param name="expression">
    ///     An <see cref="Expression" /> that represents the LINQ query to be evaluated.
    /// </param>
    /// <returns>
    ///     An <see cref="IQueryable{T}" /> that can evaluate the query represented by the specified expression tree.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the queryable instance cannot be created.
    /// </exception>
    /// <exception cref="TargetInvocationException">
    ///     Thrown when there is an error during the creation of the queryable instance.
    /// </exception>
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new SanityDocumentSet<TElement>(this, expression);
    }

    public object Execute(Expression expression)
    {
        return Execute<object>(expression);
    }

    /// <summary>
    ///     Executes the query represented by the specified expression tree and returns the result.
    /// </summary>
    /// <typeparam name="TResult">
    ///     The type of the result expected from the execution of the query.
    /// </typeparam>
    /// <param name="expression">
    ///     An <see cref="Expression" /> that represents the LINQ query to be executed.
    /// </param>
    /// <returns>
    ///     The result of executing the query represented by the specified expression tree.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the query cannot be executed.
    /// </exception>
    /// <exception cref="AggregateException">
    ///     Thrown when an exception occurs during the execution of the query.
    /// </exception>
    public TResult Execute<TResult>(Expression expression)
    {
        return ExecuteWithCallback<TResult>(expression, null);
    }

    public TResult ExecuteWithCallback<TResult>(Expression expression, ContentCallback? callback = null)
    {
        return ExecuteWithCallbackAsync<TResult>(expression, callback).GetAwaiter().GetResult();
    }
    
    /// <summary>
    ///     Asynchronously executes the specified query expression and returns the result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result expected from the query execution.</typeparam>
    /// <param name="expression">The LINQ expression representing the query to be executed.</param>
    /// <param name="cancellationToken">
    ///     A <see cref="CancellationToken" /> that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the query result of type
    ///     <typeparamref name="TResult" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="expression" /> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the query execution fails.</exception>
    public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        return ExecuteWithCallbackAsync<TResult>(expression, null, cancellationToken);
    }

    public async Task<TResult> ExecuteWithCallbackAsync<TResult>(Expression expression, ContentCallback? callback = null, CancellationToken cancellationToken = default)
    {
        var query = GetSanityQuery<TResult>(expression);

        // Execute query
        var result = await Context.Client.FetchAsync<TResult>(query, null, callback, cancellationToken).ConfigureAwait(false);

        return result.Result;
    }
    
    /// <summary>
    ///     Generates a Sanity query string based on the specified LINQ expression.
    /// </summary>
    /// <typeparam name="TResult">
    ///     The type of the result expected from the query.
    /// </typeparam>
    /// <param name="expression">
    ///     An <see cref="Expression" /> representing the LINQ query to be translated into a Sanity query string.
    /// </param>
    /// <returns>
    ///     A <see cref="string" /> containing the Sanity query.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the query cannot be parsed or translated into a valid Sanity query.
    /// </exception>
    public string GetSanityQuery<TResult>(Expression expression)
    {
        var parser = new SanityExpressionParser(expression, DocType, MaxNestingLevel, typeof(TResult));

        var query = parser.BuildQuery();

        return PrettyPrintQuery(query);
    }

    internal static string PrettyPrintQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;

        var sb = new StringBuilder();
        var indentLevel = 0;
        var bracketLevel = 0;
        var parenLevel = 0;
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];

            if (c is '"' or '\'' && (i == 0 || query[i - 1] != '\\'))
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (c == quoteChar)
                {
                    inQuotes = false;
                }
            }

            if (inQuotes)
            {
                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '{':
                    var hadSpace = sb.Length > 0 && char.IsWhiteSpace(sb[^1]);
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
                    if (hadSpace && sb.Length > 0 && sb[^1] != '\n') sb.Append(' ');
                    sb.Append(c);
                    sb.AppendLine();
                    indentLevel++;
                    sb.Append(new string(' ', indentLevel * 2));
                    break;
                case '}':
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
                    sb.AppendLine();
                    indentLevel--;
                    sb.Append(new string(' ', indentLevel * 2));
                    sb.Append(c);
                    break;
                case ',':
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
                    sb.Append(c);
                    if (bracketLevel == 0 && parenLevel == 0 && indentLevel > 0)
                    {
                        sb.AppendLine();
                        sb.Append(new string(' ', indentLevel * 2));
                        // Skip the next whitespace if we just added a newline
                        if (i + 1 < query.Length && char.IsWhiteSpace(query[i + 1])) i++;
                    }

                    break;
                case '[':
                    bracketLevel++;
                    sb.Append(c);
                    break;
                case ']':
                    bracketLevel--;
                    sb.Append(c);
                    break;
                case '(':
                    parenLevel++;
                    sb.Append(c);
                    break;
                case ')':
                    parenLevel--;
                    sb.Append(c);
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && sb[^1] != '\n') sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString().Trim();
    }
}