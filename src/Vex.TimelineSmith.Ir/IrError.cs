namespace Vex.TimelineSmith.Ir;

public sealed record IrError(string Code, string Path, string Message)
{
    public override string ToString() => $"{Code} @ {Path}: {Message}";
}

public readonly struct IrResult<T>
{
    public T? Value { get; }
    public IrError[] Errors { get; }
    public bool IsSuccess => Errors.Length == 0 && Value is not null;

    private IrResult(T? value, IrError[] errors)
    {
        Value = value;
        Errors = errors;
    }

    public static IrResult<T> Ok(T value) => new(value, Array.Empty<IrError>());

    public static IrResult<T> Fail(params IrError[] errors) => new(default, errors);

    public static IrResult<T> Fail(IEnumerable<IrError> errors) => new(default, errors.ToArray());
}
