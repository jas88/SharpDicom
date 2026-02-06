// NccidPatches.cs
//
// This file contains the DICOM-relevant parts of the nccid project
// (https://github.com/jas88/nccid), compiled against SharpDicom.FoDicom5.Compat
// instead of the fo-dicom NuGet package.
//
// PURPOSE: Proves the compat layer's network adapter is a drop-in replacement
// for fo-dicom's networking API as used by nccid (C-FIND queries to PACS).
//
// PATCHES APPLIED:
// 1. Extracted DICOM-specific query logic from nccid/nccidmain.cs Search method
//    into a standalone NccidSearch class. The original method also depends on
//    CsvHelper, Amazon.S3, CommandLine, JetBrains.Annotations, and
//    System.IO.Abstractions which are unrelated to DICOM migration.
// 2. Removed CSV/S3/filesystem dependencies. The query construction and
//    response handling logic is identical to nccid source.
// 3. No changes to fo-dicom API usage - all of these compile unmodified:
//    - DicomClientFactory.Create(host, port, useTls, callingAE, calledAE)
//    - client.NegotiateAsyncOps() (no-op, matches fo-dicom behavior)
//    - client.AddRequestAsync(DicomCFindRequest)
//    - client.SendAsync()
//    - new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
//    - request.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192")
//    - request.Dataset.AddOrUpdate(DicomTag.StudyDate, dateRange)
//    - request.Dataset.AddOrUpdate(DicomTag.PatientID, pseudonym)
//    - request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "")
//    - request.OnResponseReceived += (req, resp) => { ... }
//    - resp.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID)

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace SharpDicom.Migration.Integration;

/// <summary>
/// Extracted from nccid/nccidmain.cs - the DICOM query logic for searching
/// a PACS for studies matching patient criteria.
/// Uses fo-dicom API surface via SharpDicom.FoDicom5.Compat.
/// </summary>
public sealed class NccidSearch
{
    /// <summary>
    /// Represents a patient query with pseudonym and date window,
    /// matching nccid's NccidData/PositiveData/NegativeData.
    /// </summary>
    public sealed class PatientQuery
    {
        /// <summary>Gets the patient pseudonym (CHI number or equivalent).</summary>
        public string Pseudonym { get; }

        /// <summary>Gets the DICOM date range string for the study window.</summary>
        public string DateRange { get; }

        /// <summary>
        /// Creates a patient query.
        /// </summary>
        public PatientQuery(string pseudonym, string dateRange)
        {
            Pseudonym = pseudonym;
            DateRange = dateRange;
        }
    }

    /// <summary>
    /// Queries a PACS for studies matching the given patient criteria.
    /// This is the exact DICOM logic from nccid's Search method,
    /// exercising DicomClientFactory, DicomCFindRequest, and the
    /// request-queue pattern (AddRequestAsync + SendAsync).
    /// </summary>
    /// <param name="host">PACS hostname.</param>
    /// <param name="port">PACS port.</param>
    /// <param name="callingAE">Our AE title.</param>
    /// <param name="calledAE">PACS AE title.</param>
    /// <param name="patients">Patients to query.</param>
    /// <returns>Dictionary mapping pseudonym to list of study instance UIDs found.</returns>
    public static async Task<Dictionary<string, List<string>>> SearchPacs(
        string host, int port, string callingAE, string calledAE,
        IEnumerable<PatientQuery> patients)
    {
        // Exact nccid pattern: create client via factory, negotiate async ops
        var pacs = DicomClientFactory.Create(host, port, false, callingAE, calledAE);
#pragma warning disable CS4014 // nccid calls NegotiateAsyncOps without await (returns completed task)
        pacs.NegotiateAsyncOps();
#pragma warning restore CS4014

        var results = new Dictionary<string, List<string>>();

        foreach (var pt in patients)
        {
            var studies = new List<string>();

            // Exact nccid pattern: construct C-FIND request at Study level
            var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

            // Exact nccid pattern: set Specific Character Set via raw tag constructor
            req.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192");

            // Exact nccid pattern: set query keys
            req.Dataset.AddOrUpdate(DicomTag.StudyDate, pt.DateRange);
            req.Dataset.AddOrUpdate(DicomTag.PatientID, pt.Pseudonym);
            req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");

            // Exact nccid pattern: OnResponseReceived callback
            req.OnResponseReceived += (_, resp) =>
            {
                var uid = resp.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID);
                if (uid != null)
                    studies.Add(uid);
            };

            await pacs.AddRequestAsync(req);
            await pacs.SendAsync();

            if (studies.Count > 0)
                results[pt.Pseudonym] = studies;
        }

        return results;
    }

    /// <summary>
    /// Constructs a single C-FIND request matching nccid's query pattern.
    /// Useful for unit-testing query construction without network.
    /// </summary>
    public static DicomCFindRequest BuildQueryRequest(string pseudonym, string dateRange)
    {
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        req.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192");
        req.Dataset.AddOrUpdate(DicomTag.StudyDate, dateRange);
        req.Dataset.AddOrUpdate(DicomTag.PatientID, pseudonym);
        req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");
        return req;
    }

    /// <summary>
    /// DICOM date formatting matching nccid's Utils.DicomDate.
    /// </summary>
    public static string DicomDate(DateTime t)
        => t.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// DICOM date range formatting matching nccid's Utils.DicomWindow.
    /// </summary>
    public static string DicomWindow(DateTime t, int preyears, int pre, int? post)
        => $"{DicomDate(t.AddYears(-preyears).AddDays(-pre))}-{(post.HasValue ? DicomDate(t.AddDays(post.Value)) : "")}";
}
