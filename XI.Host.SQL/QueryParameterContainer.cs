using XI.Host.Common;
using System;

namespace XI.Host.SQL
{
    public class QueryParameterContainer
    {
        public byte[] Statement { get; set; }
        public Couple<Type>[] Columns { get; set; }
        public Couple<object>[] Parameters { get; set; }

        public QueryParameterContainer() { }

        public QueryParameterContainer(in byte[] statement, in Couple<Type>[] columns, params Couple<object>[] parameters)
        {
            Statement = statement;
            Columns = columns;
            Parameters = parameters;
        }
    }
}
