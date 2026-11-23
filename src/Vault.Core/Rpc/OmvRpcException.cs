namespace Vault.Core.Rpc;

public enum OmvRpcErrorKind
{
    /// <summary>No answer: the connection was refused, the host is unknown, or the time limit passed.</summary>
    Unreachable,

    /// <summary>OMV refused the credentials.</summary>
    AuthFailed,

    /// <summary>The session is no longer valid. The client renews it once by itself; it only reaches a caller as <see cref="AuthFailed"/>.</summary>
    SessionExpired,

    /// <summary>OMV answered with an RPC error.</summary>
    RpcError,

    /// <summary>OMV answered, but not in the shape Vault expects.</summary>
    BadResponse,
}

/// <summary>
/// A failure to get an answer from OMV. Its message is for the logs (it may carry what OMV said); what a client sees is
/// built from <see cref="Kind"/> and <see cref="RpcCode"/> only (<see cref="Models.Messages.ForRpc"/>).
/// </summary>
public sealed class OmvRpcException(OmvRpcErrorKind kind, string message, int? rpcCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public OmvRpcErrorKind Kind { get; } = kind;

    /// <summary>The <c>code</c> of the error object OMV returned, when there was one.</summary>
    public int? RpcCode { get; } = rpcCode;
}
