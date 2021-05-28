﻿/// LINQ query builders
module Dapper.FSharp.LinqBuilders

open System.Linq.Expressions
open System
open System.Collections.Generic

[<AutoOpen>]
module FQ = 
    /// Fully qualified entity type name
    type [<Struct>] FQName = private FQName of string
    let fqName (t: Type) = FQName t.FullName

type TableMapping = { Name: string; Schema: string option }

/// Fully qualifies a column with: {?schema}.{table}.{column}
let private fullyQualifyColumn (tables: Map<FQName, TableMapping>) (property: Reflection.MemberInfo) =
    let tbl = tables.[fqName property.DeclaringType]
    match tbl.Schema with
    | Some schema -> sprintf "%s.%s.%s" schema tbl.Name property.Name
    | None -> sprintf "%s.%s" tbl.Name property.Name

type QuerySource<'T>(tableMappings) =
    interface IEnumerable<'T> with
        member this.GetEnumerator() = Seq.empty<'T>.GetEnumerator() :> Collections.IEnumerator
        member this.GetEnumerator() = Seq.empty<'T>.GetEnumerator()
    
    member this.TableMappings : Map<FQName, TableMapping> = tableMappings
    
    member this.GetOuterTableMapping() = 
        let outerEntity = typeof<'T>
        let fqn = 
            if outerEntity.Name.StartsWith "Tuple" // True for joined tables
            then outerEntity.GetGenericArguments() |> Array.head |> fqName
            else outerEntity |> fqName
        this.TableMappings.[fqn]

type QuerySource<'T, 'Query>(query, tableMappings) = 
    inherit QuerySource<'T>(tableMappings)
    member this.Query : 'Query = query

[<AutoOpen>]
module Table = 

    /// Maps the entity 'T to a table of the exact same name.
    let table<'T> = 
        let ent = typeof<'T>
        let tables = Map [fqName ent, { Name = ent.Name; Schema = None }]
        QuerySource<'T>(tables)

    /// Maps the entity 'T to a table of the given name.
    let table'<'T> (tableName: string) = 
        let ent = typeof<'T>
        let tables = Map [fqName ent, { Name = tableName; Schema = None }]
        QuerySource<'T>(tables)

    /// Maps the entity 'T to a schema of the given name.
    let inSchema<'T> (schemaName: string) (qs: QuerySource<'T>) =
        let ent = typeof<'T>
        let fqn = fqName ent
        let tbl = qs.TableMappings.[fqn]
        let tables = qs.TableMappings.Add(fqn, { tbl with Schema = Some schemaName })
        QuerySource<'T>(tables)

type SelectExpressionBuilder<'T>() =

    let getQueryOrDefault (state: QuerySource<'Result>) = // 'Result allows 'T to vary as the result of joins
        match state with
        | :? QuerySource<'Result, SelectQuery> as qs -> qs.Query
        | _ -> 
            { Schema = None
              Table = ""
              Where = Where.Empty
              OrderBy = []
              Pagination = { Skip = 0; Take = None }
              Joins = []
              Aggregates = []
              GroupBy = []
              Distinct = false } : SelectQuery    

    let mergeTableMappings (a: Map<FQName, TableMapping>, b: Map<FQName, TableMapping>) =
        Map (Seq.concat [ (Map.toSeq a); (Map.toSeq b) ])

    member this.For (state: QuerySource<'T>, f: 'T -> QuerySource<'T>) =
        let tbl = state.GetOuterTableMapping()
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Table = tbl.Name; Schema = tbl.Schema }, state.TableMappings)

    member this.Yield _ =
        QuerySource<'T>(Map.empty)

    /// Sets the WHERE condition
    [<CustomOperation("where", MaintainsVariableSpace = true)>]
    member this.Where (state:QuerySource<'T>, [<ProjectionParameter>] whereExpression) = 
        let query = state |> getQueryOrDefault
        let where = LinqExpressionVisitors.visitWhere<'T> whereExpression (fullyQualifyColumn state.TableMappings)
        QuerySource<'T, SelectQuery>({ query with Where = where }, state.TableMappings)

    /// Sets the ORDER BY for single column
    [<CustomOperation("orderBy", MaintainsVariableSpace = true)>]
    member this.OrderBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
        let orderBy = OrderBy (propertyName, Asc)
        QuerySource<'T, SelectQuery>({ query with OrderBy = query.OrderBy @ [orderBy] }, state.TableMappings)

    /// Sets the ORDER BY for single column
    [<CustomOperation("thenBy", MaintainsVariableSpace = true)>]
    member this.ThenBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
        let orderBy = OrderBy (propertyName, Asc)
        QuerySource<'T, SelectQuery>({ query with OrderBy = query.OrderBy @ [orderBy] }, state.TableMappings)

    /// Sets the ORDER BY DESC for single column
    [<CustomOperation("orderByDescending", MaintainsVariableSpace = true)>]
    member this.OrderByDescending (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
        let orderBy = OrderBy (propertyName, Desc)
        QuerySource<'T, SelectQuery>({ query with OrderBy = query.OrderBy @ [orderBy] }, state.TableMappings)

    /// Sets the ORDER BY DESC for single column
    [<CustomOperation("thenByDescending", MaintainsVariableSpace = true)>]
    member this.ThenByDescending (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
        let orderBy = OrderBy (propertyName, Desc)
        QuerySource<'T, SelectQuery>({ query with OrderBy = query.OrderBy @ [orderBy] }, state.TableMappings)

    /// Sets the SKIP value for query
    [<CustomOperation("skip", MaintainsVariableSpace = true)>]
    member this.Skip (state:QuerySource<'T>, skip) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Pagination = { query.Pagination with Skip = skip } }, state.TableMappings)
    
    /// Sets the TAKE value for query
    [<CustomOperation("take", MaintainsVariableSpace = true)>]
    member this.Take (state:QuerySource<'T>, take) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Pagination = { query.Pagination with Take = Some take } }, state.TableMappings)

    /// Sets the SKIP and TAKE value for query
    [<CustomOperation("skipTake", MaintainsVariableSpace = true)>]
    member this.SkipTake (state:QuerySource<'T>, skip, take) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Pagination = { query.Pagination with Skip = skip; Take = Some take } }, state.TableMappings)

    /// INNER JOIN table where COLNAME equals to another COLUMN (including TABLE name)
    [<CustomOperation("join", MaintainsVariableSpace = true, IsLikeJoin = true, JoinConditionWord = "on")>]
    member this.Join (outerSource: QuerySource<'TOuter>, 
                      innerSource: QuerySource<'TInner>, 
                      outerKeySelector: Expression<Func<'TOuter,'Key>>, 
                      innerKeySelector: Expression<Func<'TInner,'Key>>, 
                      resultSelector: Expression<Func<'TOuter,'TInner,'Result>> ) = 

        let mergedTables = mergeTableMappings (outerSource.TableMappings, innerSource.TableMappings)
        let outerPropertyName = LinqExpressionVisitors.visitPropertySelector<'TOuter, 'Key> outerKeySelector |> fullyQualifyColumn mergedTables
        
        // Do not fully qualify inner column name because Dapper.FSharp later appends "{innerTableName}.{innerPropertyName}"
        let innerProperty = LinqExpressionVisitors.visitPropertySelector<'TInner, 'Key> innerKeySelector
        let innerTableName = 
            let tbl = mergedTables.[fqName innerProperty.DeclaringType]
            match tbl.Schema with
            | Some schema -> sprintf "%s.%s" schema tbl.Name
            | None -> tbl.Name

        let join = InnerJoin (innerTableName, innerProperty.Name, outerPropertyName)
        let outerQuery = outerSource |> getQueryOrDefault
        QuerySource<'Result, SelectQuery>({ outerQuery with Joins = outerQuery.Joins @ [join] }, mergedTables)

    /// LEFT JOIN table where COLNAME equals to another COLUMN (including TABLE name)
    [<CustomOperation("leftJoin", MaintainsVariableSpace = true, IsLikeJoin = true, JoinConditionWord = "on")>]
    member this.LeftJoin (outerSource: QuerySource<'TOuter>, 
                          innerSource: QuerySource<'TInner>, 
                          outerKeySelector: Expression<Func<'TOuter,'Key>>, 
                          innerKeySelector: Expression<Func<'TInner,'Key>>, 
                          resultSelector: Expression<Func<'TOuter,'TInner,'Result>> ) = 

        let mergedTables = mergeTableMappings (outerSource.TableMappings, innerSource.TableMappings)
        let outerPropertyName = LinqExpressionVisitors.visitPropertySelector<'TOuter, 'Key> outerKeySelector |> fullyQualifyColumn mergedTables
        
        // Do not fully qualify inner column name because Dapper.FSharp later appends "{innerTableName}.{innerPropertyName}"
        let innerProperty = LinqExpressionVisitors.visitPropertySelector<'TInner, 'Key> innerKeySelector
        let innerTableName = 
            let tbl = mergedTables.[fqName innerProperty.DeclaringType]
            match tbl.Schema with
            | Some schema -> sprintf "%s.%s" schema tbl.Name
            | None -> tbl.Name

        let join = LeftJoin (innerTableName, innerProperty.Name, outerPropertyName)
        let outerQuery = outerSource |> getQueryOrDefault
        QuerySource<'Result, SelectQuery>({ outerQuery with Joins = outerQuery.Joins @ [join] }, mergedTables)

    /// Sets the GROUP BY for one or more columns.
    [<CustomOperation("groupBy", MaintainsVariableSpace = true)>]
    member this.GroupBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let properties = LinqExpressionVisitors.visitGroupBy<'T, 'Prop> propertySelector (fullyQualifyColumn state.TableMappings)
        QuerySource<'T, SelectQuery>({ query with GroupBy = query.GroupBy @ properties}, state.TableMappings)

    /// COUNT aggregate function for COLNAME (or * symbol) and map it to ALIAS
    [<CustomOperation("count", MaintainsVariableSpace = true)>]
    member this.Count (state:QuerySource<'T>, colName, alias) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Count(colName, alias)] }, state.TableMappings)

    /// COUNT aggregate function for the selected column
    [<CustomOperation("countBy", MaintainsVariableSpace = true)>]
    member this.CountBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Count(propertyName, propertyName)] }, state.TableMappings)

    /// AVG aggregate function for COLNAME (or * symbol) and map it to ALIAS
    [<CustomOperation("avg", MaintainsVariableSpace = true)>]
    member this.Avg (state:QuerySource<'T>, colName, alias) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Avg(colName, alias)] }, state.TableMappings)

    /// AVG aggregate function for the selected column
    //[<CustomOperation("avgBy", MaintainsVariableSpace = true)>]
    //member this.AvgBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
    //    let query = state |> getQueryOrDefault
    //    let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
    //    QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Avg(propertyName, propertyName)] }, state.TableMappings)
    
    /// SUM aggregate function for COLNAME (or * symbol) and map it to ALIAS
    [<CustomOperation("sum", MaintainsVariableSpace = true)>]
    member this.Sum (state:QuerySource<'T>, colName, alias) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Sum(colName, alias)] }, state.TableMappings)

    /// SUM aggregate function for the selected column
    //[<CustomOperation("sumBy", MaintainsVariableSpace = true)>]
    //member this.SumBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
    //    let query = state |> getQueryOrDefault
    //    let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
    //    QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Sum(propertyName, propertyName)] }, state.TableMappings)
    
    /// MIN aggregate function for COLNAME (or * symbol) and map it to ALIAS
    [<CustomOperation("min", MaintainsVariableSpace = true)>]
    member this.Min (state:QuerySource<'T>, colName, alias) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Min(colName, alias)] }, state.TableMappings)

    /// MIN aggregate function for the selected column
    //[<CustomOperation("minBy", MaintainsVariableSpace = true)>]
    //member this.MinBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
    //    let query = state |> getQueryOrDefault
    //    let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
    //    QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Min(propertyName, propertyName)] }, state.TableMappings)
    
    /// MIN aggregate function for COLNAME (or * symbol) and map it to ALIAS
    [<CustomOperation("max", MaintainsVariableSpace = true)>]
    member this.Max (state:QuerySource<'T>, colName, alias) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Max(colName, alias)] }, state.TableMappings)

    /// MIN aggregate function for the selected column
    //[<CustomOperation("maxBy", MaintainsVariableSpace = true)>]
    //member this.MaxBy (state:QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
    //    let query = state |> getQueryOrDefault
    //    let propertyName = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector |> fullyQualifyColumn state.TableMappings
    //    QuerySource<'T, SelectQuery>({ query with Aggregates = query.Aggregates @ [Aggregate.Max(propertyName, propertyName)] }, state.TableMappings)
    
    /// Sets query to return DISTINCT values
    [<CustomOperation("distinct", MaintainsVariableSpace = true)>]
    member this.Distinct (state:QuerySource<'T>) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, SelectQuery>({ query with Distinct = true }, state.TableMappings)

    /// Selects all (needed only when there are no other clauses after "for" or "join")
    [<CustomOperation("selectAll", MaintainsVariableSpace = true)>]
    member this.SelectAll (state:QuerySource<'T>) = 
        state :?> QuerySource<'T, SelectQuery>

    /// Unwraps the query
    member this.Run (state: QuerySource<'T>) =
        state |> getQueryOrDefault

type DeleteExpressionBuilder<'T>() =

    let getQueryOrDefault (state: QuerySource<'Result>) =
        match state with
        | :? QuerySource<'Result, DeleteQuery> as qs -> qs.Query
        | _ -> 
            { Schema = None
              Table = ""
              Where = Where.Empty } : DeleteQuery

    member this.For (state: QuerySource<'T>, f: 'T -> QuerySource<'T>) =
        let tbl = state.GetOuterTableMapping()
        let query = state |> getQueryOrDefault
        QuerySource<'T, DeleteQuery>({ query with Table = tbl.Name; Schema = tbl.Schema }, state.TableMappings)

    member this.Yield _ =
        QuerySource<'T>(Map.empty)

    /// Sets the WHERE condition
    [<CustomOperation("where", MaintainsVariableSpace = true)>]
    member this.Where (state:QuerySource<'T>, [<ProjectionParameter>] whereExpression) = 
        let query = state |> getQueryOrDefault
        let where = LinqExpressionVisitors.visitWhere<'T> whereExpression (fullyQualifyColumn state.TableMappings)
        QuerySource<'T, DeleteQuery>({ query with Where = where }, state.TableMappings)

    /// Deletes all records in the table (only when there are is no where clause)
    [<CustomOperation("deleteAll", MaintainsVariableSpace = true)>]
    member this.DeleteAll (state:QuerySource<'T>) = 
        state :?> QuerySource<'T, DeleteQuery>

    /// Unwraps the query
    member this.Run (state: QuerySource<'T>) =
        state |> getQueryOrDefault

type InsertExpressionBuilder<'T>() =

    let getQueryOrDefault (state: QuerySource<'Result>) =
        match state with
        | :? QuerySource<'Result, InsertQuery<'T>> as qs -> qs.Query
        | _ -> 
            { Schema = None
              Table = ""
              Fields = None
              Values = [] } : InsertQuery<'T>

    member this.For (state: QuerySource<'T>, f: 'T -> QuerySource<'T>) =
        let tbl = state.GetOuterTableMapping()
        let query = state |> getQueryOrDefault
        QuerySource<'T, InsertQuery<'T>>({ query with Table = tbl.Name; Schema = tbl.Schema }, state.TableMappings)

    /// Sets the TABLE name for query.
    [<CustomOperation("into")>]
    member this.Into (state: QuerySource<'T>, table: QuerySource<'T>) =
        let tbl = table.GetOuterTableMapping()
        let query = state |> getQueryOrDefault
        QuerySource<'T, InsertQuery<'T>>({ query with Table = tbl.Name; Schema = tbl.Schema }, state.TableMappings)

    member this.Yield _ =
        QuerySource<'T>(Map.empty)

    /// Sets the list of values for INSERT
    [<CustomOperation("values", MaintainsVariableSpace = true)>]
    member this.Values (state: QuerySource<'T>, values:'T list) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, InsertQuery<'T>>({ query with Values = values }, state.TableMappings)

    /// Sets the single value for INSERT
    [<CustomOperation("value", MaintainsVariableSpace = true)>]
    member this.Value (state:QuerySource<'T>, value:'T) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, InsertQuery<'T>>({ query with Values = [value] }, state.TableMappings)

    /// Includes a column in the insert query.
    [<CustomOperation("column", MaintainsVariableSpace = true)>]
    member this.Column (state: QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let prop = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector
        let newQuery = 
            match query.Fields with
            | Some fields  -> { query with Fields = Some (fields @ [prop.Name]) }
            | None -> { query with Fields = Some [prop.Name] }

        QuerySource<'T, InsertQuery<'T>>(newQuery, state.TableMappings)

    /// Excludes a column from the insert query.
    [<CustomOperation("exclude", MaintainsVariableSpace = true)>]
    member this.Exclude (state: QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let prop = LinqExpressionVisitors.visitPropertySelector<'T, 'Prop> propertySelector
        let newQuery = 
            match query.Fields with
            | Some fields  -> 
                let filteredFields = fields |> List.filter (fun f -> f <> prop.Name)
                { query with Fields = Some filteredFields }
            | None -> 
                let filteredFields = Reflection.getFields typeof<'T> |> List.filter (fun f -> f <> prop.Name)
                { query with Fields = Some filteredFields }

        QuerySource<'T, InsertQuery<'T>>(newQuery, state.TableMappings)

    /// Unwraps the query
    member this.Run (state: QuerySource<'T>) =
        state |> getQueryOrDefault

type UpdateExpressionBuilder<'T, 'U>() =
    
    let getQueryOrDefault (state: QuerySource<'Result>) =
        match state with
        | :? QuerySource<'Result, UpdateQuery<'U>> as qs -> qs.Query
        | _ -> 
            { Schema = None
              Table = ""
              Value = Unchecked.defaultof<'U>
              Fields = None
              Where = Where.Empty } : UpdateQuery<'U>

    member this.For (state: QuerySource<'T>, f: 'T -> QuerySource<'T>) =
        let tbl = state.GetOuterTableMapping()
        let query = state |> getQueryOrDefault
        QuerySource<'T, UpdateQuery<'U>>({ query with Table = tbl.Name; Schema = tbl.Schema }, state.TableMappings)

    member this.Yield _ =
        QuerySource<'T>(Map.empty)

    /// Sets the SET of value ('U) to UPDATE
    [<CustomOperation("set", MaintainsVariableSpace = true)>]
    member this.Set (state: QuerySource<'T>, value: 'U) = 
        let query = state |> getQueryOrDefault
        QuerySource<'T, UpdateQuery<'U>>({ query with Value = value }, state.TableMappings)

    /// Includes a column in the update query.
    [<CustomOperation("column", MaintainsVariableSpace = true)>]
    member this.Column (state: QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let prop = LinqExpressionVisitors.visitPropertySelector<'U, 'Prop> propertySelector
        let newQuery = 
            match query.Fields with
            | Some fields  -> { query with Fields = Some (fields @ [prop.Name]) }
            | None -> { query with Fields = Some [prop.Name] }

        QuerySource<'T, UpdateQuery<'U>>(newQuery, state.TableMappings)

    /// Excludes a column from the update query.
    [<CustomOperation("exclude", MaintainsVariableSpace = true)>]
    member this.Exclude (state: QuerySource<'T>, [<ProjectionParameter>] propertySelector) = 
        let query = state |> getQueryOrDefault
        let prop = LinqExpressionVisitors.visitPropertySelector<'U, 'Prop> propertySelector
        let newQuery = 
            match query.Fields with
            | Some fields  -> 
                let filteredFields = fields |> List.filter (fun f -> f <> prop.Name)
                { query with Fields = Some filteredFields }
            | None -> 
                let filteredFields = Reflection.getFields typeof<'T> |> List.filter (fun f -> f <> prop.Name)
                { query with Fields = Some filteredFields }

        QuerySource<'T, UpdateQuery<'U>>(newQuery, state.TableMappings)

    /// Sets the WHERE condition
    [<CustomOperation("where", MaintainsVariableSpace = true)>]
    member this.Where (state: QuerySource<'T>, [<ProjectionParameter>] whereExpression) = 
        let query = state |> getQueryOrDefault
        let where = LinqExpressionVisitors.visitWhere<'T> whereExpression (fullyQualifyColumn state.TableMappings)
        QuerySource<'T, UpdateQuery<'U>>({ query with Where = where }, state.TableMappings)

    /// Unwraps the query
    member this.Run (state: QuerySource<'T>) =
        state |> getQueryOrDefault

let select<'T> = SelectExpressionBuilder<'T>()
let delete<'T> = DeleteExpressionBuilder<'T>()
let insert<'T> = InsertExpressionBuilder<'T>()
let update<'T, 'U> = UpdateExpressionBuilder<'T, 'U>()

/// WHERE column is IN values
let isIn<'P> (prop: 'P) (values: 'P list) = true
/// WHERE column is NOT IN values
let isNotIn<'P> (prop: 'P) (values: 'P list) = true
/// WHERE column like value   
let like<'P> (prop: 'P) (pattern: string) = true
/// WHERE column not like value   
let notLike<'P> (prop: 'P) (pattern: string) = true
/// WHERE column IS NULL
let isNullValue<'P> (prop: 'P) = true
/// WHERE column IS NOT NULL
let isNotNullValue<'P> (prop: 'P) = true