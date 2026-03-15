using System.Linq.Expressions;
using System.Reflection;

namespace LangAngo.CSharp.Instrumentation;

public static class PropertyFetcher
{
    private static readonly Dictionary<string, Func<object, object?>> _cache = new();

    public static object? FetchProperty(object target, string propertyName)
    {
        if (target == null) return null;

        var key = $"{target.GetType().FullName}.{propertyName}";
        
        if (!_cache.TryGetValue(key, out var fetcher))
        {
            var type = target.GetType();
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            
            if (property == null)
            {
                _cache[key] = _ => null;
                return null;
            }

            var param = Expression.Parameter(typeof(object));
            var convert = Expression.Convert(param, type);
            var propertyAccess = Expression.Property(convert, property);
            var convertBack = Expression.Convert(propertyAccess, typeof(object));
            
            var lambda = Expression.Lambda<Func<object, object?>>(convertBack, param);
            fetcher = lambda.Compile();
            _cache[key] = fetcher;
        }

        try
        {
            return fetcher(target);
        }
        catch
        {
            return null;
        }
    }
}
