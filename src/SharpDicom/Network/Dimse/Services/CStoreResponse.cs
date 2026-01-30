using System;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services;

/// <summary>
/// Represents the response from a C-STORE operation.
/// </summary>
/// <remarks>
/// <para>
/// CStoreResponse encapsulates the result of a C-STORE operation, including the
/// DIMSE status, the SOP Class and Instance UIDs, and any error information.
/// </para>
/// <para>
/// Common status codes for C-STORE:
/// <list type="bullet">
///   <item><description>0x0000 - Success</description></item>
///   <item><description>0xB000 - Warning: Coercion of data elements</description></item>
///   <item><description>0xB007 - Warning: Data set does not match SOP Class</description></item>
///   <item><description>0xB006 - Warning: Elements discarded</description></item>
///   <item><description>0xA700 - Refused: Out of resources</description></item>
///   <item><description>0xA900 - Error: Identifier does not match SOP Class</description></item>
///   <item><description>0xC000-0xCFFF - Error: Cannot understand</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class CStoreResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CStoreResponse"/> class.
    /// </summary>
    /// <param name="status">The DIMSE status from the response.</param>
    /// <param name="sopClassUid">The SOP Class UID.</param>
    /// <param name="sopInstanceUid">The SOP Instance UID.</param>
    /// <param name="errorComment">Optional error comment from the SCP.</param>
    public CStoreResponse(
        DicomStatus status,
        DicomUID sopClassUid,
        DicomUID sopInstanceUid,
        string? errorComment = null)
    {
        Status = status;
        SOPClassUID = sopClassUid;
        SOPInstanceUID = sopInstanceUid;
        ErrorComment = errorComment;
    }

    /// <summary>
    /// Gets the DIMSE status from the response.
    /// </summary>
    public DicomStatus Status { get; }

    /// <summary>
    /// Gets the SOP Class UID of the stored object.
    /// </summary>
    public DicomUID SOPClassUID { get; }

    /// <summary>
    /// Gets the SOP Instance UID of the stored object.
    /// </summary>
    public DicomUID SOPInstanceUID { get; }

    /// <summary>
    /// Gets the error comment if status is not success.
    /// </summary>
    /// <remarks>
    /// This corresponds to the Error Comment (0000,0902) attribute in the response.
    /// May be null if the SCP did not include an error comment.
    /// </remarks>
    public string? ErrorComment { get; }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    /// <remarks>
    /// Returns true if the status code is 0x0000.
    /// </remarks>
    public bool IsSuccess => Status.IsSuccess;

    /// <summary>
    /// Gets a value indicating whether the operation completed with a warning.
    /// </summary>
    /// <remarks>
    /// Returns true if the status code is in the warning range (0xB000-0xBFFF).
    /// Warnings indicate the data was stored but with modifications.
    /// </remarks>
    public bool IsWarning => Status.IsWarning;

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    /// <remarks>
    /// Returns true if the status code indicates a failure. The data was not stored.
    /// </remarks>
    public bool IsFailure => Status.IsFailure;

    /// <summary>
    /// Gets a value indicating whether the operation was successful or completed with a warning.
    /// </summary>
    /// <remarks>
    /// Returns true if the data was stored (possibly with modifications).
    /// </remarks>
    public bool IsSuccessOrWarning => IsSuccess || IsWarning;

    /// <summary>
    /// Returns a string representation of the response.
    /// </summary>
    public override string ToString()
    {
        var result = $"CStoreResponse {{ Status={Status}, SOPInstanceUID={SOPInstanceUID} }}";
        return ErrorComment != null ? $"{result} ({ErrorComment})" : result;
    }
}
