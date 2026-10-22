namespace GgufLauncher.Gguf;

/// <summary>
/// Raised when a file cannot be interpreted as a GGUF model, or when it uses a feature this
/// reader does not support.
/// </summary>
/// <remarks>
/// The launcher is started by a double click in Explorer, so every failure mode - a missing file,
/// a truncated download, a foreign format - is reported through this exception. Its message is
/// written for end users and can be shown as-is, and it always wraps the underlying cause when
/// there is one.
/// </remarks>
public sealed class GgufException : Exception
{
    /// <summary>Initializes a new instance with a user facing message.</summary>
    /// <param name="message">The message describing why the file could not be read.</param>
    public GgufException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a user facing message and a cause.</summary>
    /// <param name="message">The message describing why the file could not be read.</param>
    /// <param name="innerException">The underlying failure, such as an I/O error.</param>
    public GgufException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
