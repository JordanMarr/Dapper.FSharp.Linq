﻿module internal Dapper.FSharp.ExpressionVisitor

open System.Linq.Expressions
open System

let (|Lambda|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.Lambda -> Some (exp :?> LambdaExpression)
    | _ -> None

let (|Unary|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.ArrayLength
    | ExpressionType.Convert
    | ExpressionType.ConvertChecked
    | ExpressionType.Negate
    | ExpressionType.UnaryPlus
    | ExpressionType.NegateChecked
    | ExpressionType.Not
    | ExpressionType.Quote
    | ExpressionType.TypeAs -> Some (exp :?> UnaryExpression)
    | _ -> None

let (|Binary|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.Add
    | ExpressionType.AddChecked
    | ExpressionType.And
    | ExpressionType.AndAlso
    | ExpressionType.ArrayIndex
    | ExpressionType.Coalesce
    | ExpressionType.Divide
    | ExpressionType.Equal
    | ExpressionType.ExclusiveOr
    | ExpressionType.GreaterThan
    | ExpressionType.GreaterThanOrEqual
    | ExpressionType.LeftShift
    | ExpressionType.LessThan
    | ExpressionType.LessThanOrEqual
    | ExpressionType.Modulo
    | ExpressionType.Multiply
    | ExpressionType.MultiplyChecked
    | ExpressionType.NotEqual
    | ExpressionType.Or
    | ExpressionType.OrElse
    | ExpressionType.Power
    | ExpressionType.RightShift
    | ExpressionType.Subtract
    | ExpressionType.SubtractChecked -> Some (exp :?> BinaryExpression)
    | _ -> None

let (|MethodCall|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.Call -> Some (exp :?> MethodCallExpression)
    | _ -> None

let (|Constant|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.Constant -> Some (exp :?> ConstantExpression)
    | _ -> None

let (|Member|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.MemberAccess -> Some (exp :?> MemberExpression)
    | _ -> None

let (|Parameter|_|) (exp: Expression) =
    match exp.NodeType with
    | ExpressionType.Parameter -> Some (exp :?> ParameterExpression)
    | _ -> None

let getColumnComparison (expType: ExpressionType, value: obj) =
    match expType with
    | ExpressionType.Equal -> Eq value
    | ExpressionType.NotEqual -> Ne value
    | ExpressionType.GreaterThan -> Gt value
    | ExpressionType.GreaterThanOrEqual -> Ge value
    | ExpressionType.LessThan -> Lt value
    | ExpressionType.LessThanOrEqual -> Le value
    | _ -> failwith "Unsupported statement"

let rec visit (exp: Expression) : Where =
    match exp with
    | Lambda x ->
        visit x.Body

    | Unary x ->
        visit x.Operand

    | Binary x -> 
        match exp.NodeType with
        | ExpressionType.And
        | ExpressionType.AndAlso ->
            let lt = visit x.Left
            let rt = visit x.Right
            Binary (lt, And, rt)
        | ExpressionType.Or
        | ExpressionType.OrElse ->
            let lt = visit x.Left
            let rt = visit x.Right
            Binary (lt, Or, rt)
        | _ ->
            match x.Left, x.Right with
            | Member m, Constant c ->
                let colName = m.Member.Name
                let value = c.Value
                let columnComparison = getColumnComparison(exp.NodeType, value)
                Column (colName, columnComparison)
            | _ ->
                failwith "Not supported"

    | MethodCall x -> 
        failwith "Not Implemented"

    | Constant x -> 
        failwith "Not Implemented"

    | Member x ->
        failwith "Not Implemented"

    | Parameter x -> 
        failwith "Not Implemented"

    | _ ->
        failwithf "Not implemented expression type: %A" exp.NodeType

let visitWhere<'T> (filter: Expression<Func<'T, bool>>) =
    visit (filter :> Expression)