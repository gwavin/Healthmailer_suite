#define WIN32_LEAN_AND_MEAN

#include <windows.h>
#include <winspool.h>
#include <winsplp.h>
#include <sddl.h>
#include <strsafe.h>
#include <wchar.h>

/*
 * PrintRxer first-cut native port monitor.
 *
 * This exposes one fixed local port, "printrx:", and captures the raw XPS
 * payload to %ProgramData%\printrxer_v3\work\spool\<job-folder>\job.xps. When the job
 * completes, the folder is atomically moved into %ProgramData%\printrxer_v3\work\
 * incoming\<job-folder> for the managed agent to process.
 *
 * The monitor does not open network sockets or implement a remote transport.
 * Its role is limited to local capture on the workstation.
 */

static const WCHAR PRINT_RXER_PORT_NAME[] = L"printrx:";
static const WCHAR PRINT_RXER_MONITOR_NAME[] = L"PrintRxer Port Monitor";
static const WCHAR PRINT_RXER_DESCRIPTION[] = L"PrintRxer XPS capture port";
static const WCHAR PRINT_RXER_ROOT_NAME[] = L"printrxer_v3";
static const WCHAR PRINT_RXER_WORK_NAME[] = L"work";
static const WCHAR PRINT_RXER_SPOOL_NAME[] = L"spool";
static const WCHAR PRINT_RXER_INCOMING_NAME[] = L"incoming";
static const ULONGLONG PRINT_RXER_MIN_FREE_BYTES = 256ULL * 1024ULL * 1024ULL;

typedef struct _MONITOR_CONTEXT
{
    MONITOR2 Monitor2;
} MONITOR_CONTEXT, *PMONITOR_CONTEXT;

typedef struct _PORT_CONTEXT
{
    CRITICAL_SECTION Lock;
    BOOL LockInitialized;
    WCHAR PortName[32];
    WCHAR PrinterName[MAX_PATH];
    WCHAR DocumentName[MAX_PATH];
    WCHAR SubmittingUser[256];
    WCHAR SubmittingUserSid[256];
    WCHAR JobFolderName[96];
    WCHAR StagingDirectory[MAX_PATH];
    WCHAR FinalDirectory[MAX_PATH];
    WCHAR PayloadPath[MAX_PATH];
    WCHAR MetadataPath[MAX_PATH];
    HANDLE PayloadHandle;
    DWORD JobId;
    BOOL JobStarted;
    BOOL JobCommitted;
    BOOL SawData;
} PORT_CONTEXT, *PPORT_CONTEXT;

typedef struct _XCV_CONTEXT
{
    ACCESS_MASK GrantedAccess;
    WCHAR ObjectName[128];
} XCV_CONTEXT, *PXCV_CONTEXT;

static BOOL WINAPI MonitorEnumPorts(HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE pPorts, DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned);
static BOOL WINAPI MonitorOpenPort(HANDLE hMonitor, LPWSTR pName, PHANDLE pHandle);
static BOOL WINAPI MonitorOpenPortEx(HANDLE hMonitor, HANDLE hMonitorPort, LPWSTR pPortName, LPWSTR pPrinterName, PHANDLE pHandle, MONITOR2 *pMonitor2);
static BOOL WINAPI MonitorStartDocPort(HANDLE hPort, LPWSTR pPrinterName, DWORD JobId, DWORD Level, LPBYTE pDocInfo);
static BOOL WINAPI MonitorWritePort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten);
static BOOL WINAPI MonitorReadPort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuffer, LPDWORD pcbRead);
static BOOL WINAPI MonitorEndDocPort(HANDLE hPort);
static BOOL WINAPI MonitorClosePort(HANDLE hPort);
static BOOL WINAPI MonitorAddPort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pMonitorName);
static BOOL WINAPI MonitorAddPortEx(HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE lpBuffer, LPWSTR lpMonitorName);
static BOOL WINAPI MonitorConfigurePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName);
static BOOL WINAPI MonitorDeletePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName);
static BOOL WINAPI MonitorGetPrinterDataFromPort(HANDLE hPort, DWORD ControlId, LPWSTR pValueName, LPWSTR lpInBuffer, DWORD cbInBuffer, LPWSTR lpOutBuffer, DWORD cbOutBuffer, LPDWORD lpcbReturned);
static BOOL WINAPI MonitorSetPortTimeouts(HANDLE hPort, LPCOMMTIMEOUTS lpCTO, DWORD reserved);
static BOOL WINAPI MonitorXcvOpenPort(HANDLE hMonitor, LPCWSTR pszObject, ACCESS_MASK GrantedAccess, PHANDLE phXcv);
static DWORD WINAPI MonitorXcvDataPort(HANDLE hXcv, LPCWSTR pszDataName, PBYTE pInputData, DWORD cbInputData, PBYTE pOutputData, DWORD cbOutputData, PDWORD pcbOutputNeeded);
static BOOL WINAPI MonitorXcvClosePort(HANDLE hXcv);
static VOID WINAPI MonitorShutdown(HANDLE hMonitor);
static DWORD WINAPI MonitorSendRecvBidiDataFromPort(HANDLE hPort, DWORD dwAccessBit, LPCWSTR pAction, PBIDI_REQUEST_CONTAINER pReqData, PBIDI_RESPONSE_CONTAINER *ppResData);
static DWORD WINAPI MonitorNotifyUsedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR *ppszPorts);
static DWORD WINAPI MonitorNotifyUnusedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR *ppszPorts);
static DWORD WINAPI MonitorPowerEvent(HANDLE hMonitor, DWORD event, POWERBROADCAST_SETTING *pSettings);

static void TryDeleteCommittedPrintJob(PCWSTR printerName, DWORD jobId)
{
    HANDLE printerHandle = NULL;

    if (printerName == NULL || printerName[0] == L'\0' || jobId == 0)
    {
        return;
    }

    if (!OpenPrinterW((LPWSTR)printerName, &printerHandle, NULL))
    {
        return;
    }

    /* The payload has already been flushed, closed, and committed to incoming.
       Some Windows/client-side rendering combinations keep these completed jobs
       visible in the queue even when KeepPrintedJobs is disabled. Ask the
       spooler to remove only this successfully committed job; failures are
       deliberately ignored so queue cleanup can never turn a captured job into
       a spooler failure. */
    SetJobW(printerHandle, jobId, 0, NULL, JOB_CONTROL_DELETE);
    ClosePrinter(printerHandle);
}

static BOOL StringsEqualInsensitive(PCWSTR left, PCWSTR right)
{
    if (left == NULL || right == NULL)
    {
        return FALSE;
    }

    return lstrcmpiW(left, right) == 0;
}

static BOOL CopyWideString(PWSTR destination, size_t cchDestination, PCWSTR source)
{
    HRESULT hr;

    if (destination == NULL || cchDestination == 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    if (source == NULL)
    {
        destination[0] = L'\0';
        return TRUE;
    }

    hr = StringCchCopyW(destination, cchDestination, source);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    return TRUE;
}

static BOOL ResolveSidStringForAccount(PCWSTR accountName, PWSTR sidText, size_t cchSidText)
{
    DWORD sidSize = 0;
    DWORD domainSize = 0;
    SID_NAME_USE sidUse;
    PSID sid = NULL;
    PWSTR domainName = NULL;
    PWSTR resolvedSidText = NULL;
    BOOL success = FALSE;

    if (sidText == NULL || cchSidText == 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    sidText[0] = L'\0';
    if (accountName == NULL || accountName[0] == L'\0')
    {
        return FALSE;
    }

    LookupAccountNameW(NULL, accountName, NULL, &sidSize, NULL, &domainSize, &sidUse);
    if (sidSize == 0)
    {
        return FALSE;
    }

    sid = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sidSize);
    domainName = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, ((SIZE_T)domainSize + 1) * sizeof(WCHAR));
    if (sid == NULL || domainName == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        goto cleanup;
    }

    if (!LookupAccountNameW(NULL, accountName, sid, &sidSize, domainName, &domainSize, &sidUse))
    {
        goto cleanup;
    }

    if (!ConvertSidToStringSidW(sid, &resolvedSidText))
    {
        goto cleanup;
    }

    success = CopyWideString(sidText, cchSidText, resolvedSidText);

cleanup:
    if (resolvedSidText != NULL)
    {
        LocalFree(resolvedSidText);
    }
    if (domainName != NULL)
    {
        HeapFree(GetProcessHeap(), 0, domainName);
    }
    if (sid != NULL)
    {
        HeapFree(GetProcessHeap(), 0, sid);
    }

    return success;
}

static BOOL QuerySubmittingUserForJob(PCWSTR printerName, DWORD jobId, PWSTR userName, size_t cchUserName, PWSTR userSid, size_t cchUserSid)
{
    HANDLE printerHandle = NULL;
    DWORD needed = 0;
    JOB_INFO_2W *jobInfo = NULL;
    BOOL success = FALSE;

    if (userName == NULL || cchUserName == 0 || userSid == NULL || cchUserSid == 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    userName[0] = L'\0';
    userSid[0] = L'\0';
    if (printerName == NULL || printerName[0] == L'\0' || jobId == 0)
    {
        return FALSE;
    }

    if (!OpenPrinterW((LPWSTR)printerName, &printerHandle, NULL))
    {
        return FALSE;
    }

    GetJobW(printerHandle, jobId, 2, NULL, 0, &needed);
    if (needed == 0)
    {
        goto cleanup;
    }

    jobInfo = (JOB_INFO_2W *)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, needed);
    if (jobInfo == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        goto cleanup;
    }

    if (!GetJobW(printerHandle, jobId, 2, (LPBYTE)jobInfo, needed, &needed))
    {
        goto cleanup;
    }

    if (jobInfo->pUserName == NULL || jobInfo->pUserName[0] == L'\0')
    {
        goto cleanup;
    }

    if (!CopyWideString(userName, cchUserName, jobInfo->pUserName))
    {
        goto cleanup;
    }

    ResolveSidStringForAccount(userName, userSid, cchUserSid);
    success = TRUE;

cleanup:
    if (jobInfo != NULL)
    {
        HeapFree(GetProcessHeap(), 0, jobInfo);
    }
    if (printerHandle != NULL)
    {
        ClosePrinter(printerHandle);
    }

    return success;
}

static BOOL BuildSecureRootSecurityAttributes(PSECURITY_ATTRIBUTES securityAttributes, PSECURITY_DESCRIPTOR *securityDescriptor)
{
    if (securityAttributes == NULL || securityDescriptor == NULL)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    *securityDescriptor = NULL;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            L"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            SDDL_REVISION_1,
            securityDescriptor,
            NULL))
    {
        return FALSE;
    }

    securityAttributes->nLength = sizeof(SECURITY_ATTRIBUTES);
    securityAttributes->lpSecurityDescriptor = *securityDescriptor;
    securityAttributes->bInheritHandle = FALSE;
    return TRUE;
}

static BOOL EnsureSingleDirectory(PCWSTR path, PCWSTR secureRootPath)
{
    DWORD attributes;
    BOOL created;
    BOOL useSecureAttributes;
    DWORD lastError;
    SECURITY_ATTRIBUTES securityAttributes;
    PSECURITY_DESCRIPTOR securityDescriptor;

    if (path == NULL || path[0] == L'\0')
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    useSecureAttributes = secureRootPath != NULL && StringsEqualInsensitive(path, secureRootPath);
    securityDescriptor = NULL;
    ZeroMemory(&securityAttributes, sizeof(securityAttributes));
    if (useSecureAttributes)
    {
        if (!BuildSecureRootSecurityAttributes(&securityAttributes, &securityDescriptor))
        {
            return FALSE;
        }
    }

    created = CreateDirectoryW(path, useSecureAttributes ? &securityAttributes : NULL);
    lastError = GetLastError();
    if (securityDescriptor != NULL)
    {
        LocalFree(securityDescriptor);
    }

    if (created)
    {
        return TRUE;
    }

    SetLastError(lastError);
    if (lastError == ERROR_ALREADY_EXISTS)
    {
        attributes = GetFileAttributesW(path);
        if (attributes != INVALID_FILE_ATTRIBUTES &&
            (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
        {
            return TRUE;
        }

        SetLastError(ERROR_ACCESS_DENIED);
    }

    return FALSE;
}

static BOOL EnsureDirectoryTree(PCWSTR path, PCWSTR secureRootPath)
{
    WCHAR buffer[MAX_PATH];
    size_t index;
    size_t length;

    if (!CopyWideString(buffer, ARRAYSIZE(buffer), path))
    {
        return FALSE;
    }

    length = wcslen(buffer);
    if (length < 3)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    for (index = 3; index < length; ++index)
    {
        if (buffer[index] == L'\\' || buffer[index] == L'/')
        {
            WCHAR saved = buffer[index];
            buffer[index] = L'\0';

            if (!EnsureSingleDirectory(buffer, secureRootPath))
            {
                return FALSE;
            }

            buffer[index] = saved;
        }
    }

    return EnsureSingleDirectory(buffer, secureRootPath);
}

static BOOL GetProgramDataRoot(PWSTR destination, size_t cchDestination)
{
    DWORD cch;

    cch = GetEnvironmentVariableW(L"ProgramData", destination, (DWORD)cchDestination);
    if (cch == 0 || cch >= cchDestination)
    {
        return CopyWideString(destination, cchDestination, L"C:\\ProgramData");
    }

    return TRUE;
}

static BOOL EnsureMinimumFreeSpace(PCWSTR path, ULONGLONG requestedBytes)
{
    ULARGE_INTEGER freeBytesAvailable;

    if (!GetDiskFreeSpaceExW(path, &freeBytesAvailable, NULL, NULL))
    {
        return FALSE;
    }

    if (freeBytesAvailable.QuadPart < requestedBytes + PRINT_RXER_MIN_FREE_BYTES)
    {
        SetLastError(ERROR_DISK_FULL);
        return FALSE;
    }

    return TRUE;
}

static BOOL BuildCaptureRoots(PWSTR rootPath, size_t cchRootPath, PWSTR spoolPath, size_t cchSpoolPath, PWSTR incomingPath, size_t cchIncomingPath)
{
    WCHAR programDataRoot[MAX_PATH];
    HRESULT hr;

    if (!GetProgramDataRoot(programDataRoot, ARRAYSIZE(programDataRoot)))
    {
        return FALSE;
    }

    hr = StringCchPrintfW(rootPath, cchRootPath, L"%s\\%s\\%s", programDataRoot, PRINT_RXER_ROOT_NAME, PRINT_RXER_WORK_NAME);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(spoolPath, cchSpoolPath, L"%s\\%s", rootPath, PRINT_RXER_SPOOL_NAME);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(incomingPath, cchIncomingPath, L"%s\\%s", rootPath, PRINT_RXER_INCOMING_NAME);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    if (!EnsureDirectoryTree(rootPath, rootPath))
    {
        return FALSE;
    }

    if (!EnsureDirectoryTree(spoolPath, rootPath))
    {
        return FALSE;
    }

    if (!EnsureDirectoryTree(incomingPath, rootPath))
    {
        return FALSE;
    }

    return TRUE;
}

static BOOL ResetPortContextState(PPORT_CONTEXT portContext)
{
    if (portContext == NULL)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    if (portContext->PayloadHandle != INVALID_HANDLE_VALUE && portContext->PayloadHandle != NULL)
    {
        CloseHandle(portContext->PayloadHandle);
        portContext->PayloadHandle = INVALID_HANDLE_VALUE;
    }

    portContext->PrinterName[0] = L'\0';
    portContext->DocumentName[0] = L'\0';
    portContext->JobFolderName[0] = L'\0';
    portContext->StagingDirectory[0] = L'\0';
    portContext->FinalDirectory[0] = L'\0';
    portContext->PayloadPath[0] = L'\0';
    portContext->MetadataPath[0] = L'\0';
    portContext->JobId = 0;
    portContext->JobStarted = FALSE;
    portContext->JobCommitted = FALSE;
    portContext->SawData = FALSE;
    return TRUE;
}

static BOOL DeleteIfExists(PCWSTR path)
{
    DWORD attributes;

    if (path == NULL || path[0] == L'\0')
    {
        return TRUE;
    }

    attributes = GetFileAttributesW(path);
    if (attributes == INVALID_FILE_ATTRIBUTES)
    {
        return TRUE;
    }

    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
    {
        return RemoveDirectoryW(path) || GetLastError() == ERROR_PATH_NOT_FOUND;
    }

    return DeleteFileW(path) || GetLastError() == ERROR_FILE_NOT_FOUND;
}

static VOID CleanupStagingDirectory(PPORT_CONTEXT portContext)
{
    DeleteIfExists(portContext->MetadataPath);
    DeleteIfExists(portContext->PayloadPath);
    DeleteIfExists(portContext->StagingDirectory);
}

static BOOL WriteFullFile(HANDLE fileHandle, const BYTE *buffer, DWORD cbBuffer)
{
    DWORD totalWritten;

    totalWritten = 0;
    while (totalWritten < cbBuffer)
    {
        DWORD chunkWritten;
        BOOL success;

        success = WriteFile(fileHandle, buffer + totalWritten, cbBuffer - totalWritten, &chunkWritten, NULL);
        if (!success)
        {
            return FALSE;
        }

        totalWritten += chunkWritten;
    }

    return TRUE;
}

static BOOL WriteUtf8TextFile(PCWSTR path, PCWSTR text)
{
    HANDLE fileHandle;
    int bytesNeeded;
    BYTE *utf8Buffer;
    BOOL success;

    /* Metadata files are expected to be fresh staging outputs. CREATE_NEW
       avoids silently overwriting a pre-existing path. */
    fileHandle = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (fileHandle == INVALID_HANDLE_VALUE)
    {
        return FALSE;
    }

    bytesNeeded = WideCharToMultiByte(CP_UTF8, 0, text, -1, NULL, 0, NULL, NULL);
    if (bytesNeeded <= 0)
    {
        CloseHandle(fileHandle);
        SetLastError(ERROR_INVALID_DATA);
        return FALSE;
    }

    utf8Buffer = (BYTE *)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)bytesNeeded);
    if (utf8Buffer == NULL)
    {
        CloseHandle(fileHandle);
        SetLastError(ERROR_OUTOFMEMORY);
        return FALSE;
    }

    if (WideCharToMultiByte(CP_UTF8, 0, text, -1, (LPSTR)utf8Buffer, bytesNeeded, NULL, NULL) <= 0)
    {
        HeapFree(GetProcessHeap(), 0, utf8Buffer);
        CloseHandle(fileHandle);
        SetLastError(ERROR_INVALID_DATA);
        return FALSE;
    }

    success = WriteFullFile(fileHandle, utf8Buffer, (DWORD)(bytesNeeded - 1));
    HeapFree(GetProcessHeap(), 0, utf8Buffer);
    CloseHandle(fileHandle);
    return success;
}

static PWSTR JsonEscapeString(PCWSTR source)
{
    size_t length;
    size_t index;
    size_t extra;
    PWSTR escaped;
    size_t writeIndex;

    if (source == NULL)
    {
        source = L"";
    }

    length = wcslen(source);
    extra = 0;
    for (index = 0; index < length; ++index)
    {
        WCHAR ch = source[index];
        if (ch == L'\\' || ch == L'"' || ch == L'\r' || ch == L'\n' || ch == L'\t')
        {
            extra += 1;
        }
    }

    escaped = (PWSTR)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(WCHAR) * (length + extra + 1));
    if (escaped == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        return NULL;
    }

    writeIndex = 0;
    for (index = 0; index < length; ++index)
    {
        WCHAR ch = source[index];
        switch (ch)
        {
        case L'\\':
            escaped[writeIndex++] = L'\\';
            escaped[writeIndex++] = L'\\';
            break;
        case L'"':
            escaped[writeIndex++] = L'\\';
            escaped[writeIndex++] = L'"';
            break;
        case L'\r':
            escaped[writeIndex++] = L'\\';
            escaped[writeIndex++] = L'r';
            break;
        case L'\n':
            escaped[writeIndex++] = L'\\';
            escaped[writeIndex++] = L'n';
            break;
        case L'\t':
            escaped[writeIndex++] = L'\\';
            escaped[writeIndex++] = L't';
            break;
        default:
            escaped[writeIndex++] = ch;
            break;
        }
    }

    escaped[writeIndex] = L'\0';
    return escaped;
}

static BOOL WriteMetadataFile(PPORT_CONTEXT portContext)
{
    WCHAR timestamp[32];
    WCHAR metadataText[2048];
    SYSTEMTIME utcNow;
    PWSTR escapedPrinterName;
    PWSTR escapedDocumentName;
    PWSTR escapedSubmittingUser;
    PWSTR escapedSubmittingUserSid;
    HRESULT hr;

    GetSystemTime(&utcNow);
    hr = StringCchPrintfW(
        timestamp,
        ARRAYSIZE(timestamp),
        L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ",
        utcNow.wYear,
        utcNow.wMonth,
        utcNow.wDay,
        utcNow.wHour,
        utcNow.wMinute,
        utcNow.wSecond,
        utcNow.wMilliseconds);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    escapedPrinterName = JsonEscapeString(portContext->PrinterName);
    if (escapedPrinterName == NULL)
    {
        return FALSE;
    }

    escapedDocumentName = JsonEscapeString(portContext->DocumentName);
    if (escapedDocumentName == NULL)
    {
        HeapFree(GetProcessHeap(), 0, escapedPrinterName);
        return FALSE;
    }

    escapedSubmittingUser = JsonEscapeString(portContext->SubmittingUser);
    if (escapedSubmittingUser == NULL)
    {
        HeapFree(GetProcessHeap(), 0, escapedPrinterName);
        HeapFree(GetProcessHeap(), 0, escapedDocumentName);
        return FALSE;
    }

    escapedSubmittingUserSid = JsonEscapeString(portContext->SubmittingUserSid);
    if (escapedSubmittingUserSid == NULL)
    {
        HeapFree(GetProcessHeap(), 0, escapedPrinterName);
        HeapFree(GetProcessHeap(), 0, escapedDocumentName);
        HeapFree(GetProcessHeap(), 0, escapedSubmittingUser);
        return FALSE;
    }

    hr = StringCchPrintfW(
        metadataText,
        ARRAYSIZE(metadataText),
        L"{\r\n"
        L"  \"source\": \"PrintRxer.PortMonitor\",\r\n"
        L"  \"portName\": \"%s\",\r\n"
        L"  \"printerName\": \"%s\",\r\n"
        L"  \"documentName\": \"%s\",\r\n"
        L"  \"jobId\": %lu,\r\n"
        L"  \"SubmittingUser\": \"%s\",\r\n"
        L"  \"SubmittingUserSid\": \"%s\",\r\n"
        L"  \"capturedAtUtc\": \"%s\",\r\n"
        L"  \"payloadFile\": \"job.xps\"\r\n"
        L"}\r\n",
        PRINT_RXER_PORT_NAME,
        escapedPrinterName,
        escapedDocumentName,
        portContext->JobId,
        escapedSubmittingUser,
        escapedSubmittingUserSid,
        timestamp);

    HeapFree(GetProcessHeap(), 0, escapedPrinterName);
    HeapFree(GetProcessHeap(), 0, escapedDocumentName);
    HeapFree(GetProcessHeap(), 0, escapedSubmittingUser);
    HeapFree(GetProcessHeap(), 0, escapedSubmittingUserSid);

    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    return WriteUtf8TextFile(portContext->MetadataPath, metadataText);
}

static BOOL BuildJobPaths(PPORT_CONTEXT portContext, DWORD jobId)
{
    WCHAR rootPath[MAX_PATH];
    WCHAR spoolPath[MAX_PATH];
    WCHAR incomingPath[MAX_PATH];
    SYSTEMTIME utcNow;
    HRESULT hr;

    if (!BuildCaptureRoots(rootPath, ARRAYSIZE(rootPath), spoolPath, ARRAYSIZE(spoolPath), incomingPath, ARRAYSIZE(incomingPath)))
    {
        return FALSE;
    }

    GetSystemTime(&utcNow);
    hr = StringCchPrintfW(
        portContext->JobFolderName,
        ARRAYSIZE(portContext->JobFolderName),
        L"%04u%02u%02u-%02u%02u%02u%03u-job%lu",
        utcNow.wYear,
        utcNow.wMonth,
        utcNow.wDay,
        utcNow.wHour,
        utcNow.wMinute,
        utcNow.wSecond,
        utcNow.wMilliseconds,
        jobId);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(portContext->StagingDirectory, ARRAYSIZE(portContext->StagingDirectory), L"%s\\%s", spoolPath, portContext->JobFolderName);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(portContext->FinalDirectory, ARRAYSIZE(portContext->FinalDirectory), L"%s\\%s", incomingPath, portContext->JobFolderName);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(portContext->PayloadPath, ARRAYSIZE(portContext->PayloadPath), L"%s\\job.xps", portContext->StagingDirectory);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    hr = StringCchPrintfW(portContext->MetadataPath, ARRAYSIZE(portContext->MetadataPath), L"%s\\metadata.json", portContext->StagingDirectory);
    if (FAILED(hr))
    {
        SetLastError(HRESULT_CODE(hr));
        return FALSE;
    }

    if (!EnsureDirectoryTree(portContext->StagingDirectory, rootPath))
    {
        return FALSE;
    }

    return TRUE;
}

static PCWSTR GetDocumentNameFromDocInfo(DWORD level, LPBYTE pDocInfo)
{
    if (pDocInfo == NULL)
    {
        return L"";
    }

    if (level == 1)
    {
        const DOC_INFO_1W *docInfo1;
        docInfo1 = (const DOC_INFO_1W *)pDocInfo;
        return docInfo1->pDocName == NULL ? L"" : docInfo1->pDocName;
    }

    if (level == 2)
    {
        const DOC_INFO_2W *docInfo2;
        docInfo2 = (const DOC_INFO_2W *)pDocInfo;
        return docInfo2->pDocName == NULL ? L"" : docInfo2->pDocName;
    }

    return L"";
}

static BOOL PackPortInfoLevel1(LPBYTE pPorts, DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned)
{
    DWORD required;
    PORT_INFO_1W *info;
    LPWSTR cursor;
    size_t cchPortName;

    cchPortName = wcslen(PRINT_RXER_PORT_NAME) + 1;
    required = (DWORD)(sizeof(PORT_INFO_1W) + (cchPortName * sizeof(WCHAR)));
    *pcbNeeded = required;
    *pcReturned = 0;

    if (pPorts == NULL || cbBuf < required)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return FALSE;
    }

    ZeroMemory(pPorts, cbBuf);
    info = (PORT_INFO_1W *)pPorts;
    cursor = (LPWSTR)((BYTE *)pPorts + cbBuf);
    cursor -= cchPortName;
    CopyWideString(cursor, cchPortName, PRINT_RXER_PORT_NAME);
    info->pName = cursor;
    *pcReturned = 1;
    return TRUE;
}

static BOOL PackPortInfoLevel2(LPBYTE pPorts, DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned)
{
    DWORD required;
    PORT_INFO_2W *info;
    LPWSTR cursor;
    size_t cchPortName;
    size_t cchMonitorName;
    size_t cchDescription;

    cchPortName = wcslen(PRINT_RXER_PORT_NAME) + 1;
    cchMonitorName = wcslen(PRINT_RXER_MONITOR_NAME) + 1;
    cchDescription = wcslen(PRINT_RXER_DESCRIPTION) + 1;
    required = (DWORD)(sizeof(PORT_INFO_2W) + ((cchPortName + cchMonitorName + cchDescription) * sizeof(WCHAR)));
    *pcbNeeded = required;
    *pcReturned = 0;

    if (pPorts == NULL || cbBuf < required)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return FALSE;
    }

    ZeroMemory(pPorts, cbBuf);
    info = (PORT_INFO_2W *)pPorts;
    cursor = (LPWSTR)((BYTE *)pPorts + cbBuf);

    cursor -= cchDescription;
    CopyWideString(cursor, cchDescription, PRINT_RXER_DESCRIPTION);
    info->pDescription = cursor;

    cursor -= cchMonitorName;
    CopyWideString(cursor, cchMonitorName, PRINT_RXER_MONITOR_NAME);
    info->pMonitorName = cursor;

    cursor -= cchPortName;
    CopyWideString(cursor, cchPortName, PRINT_RXER_PORT_NAME);
    info->pPortName = cursor;

    info->fPortType = PORT_TYPE_WRITE;
    info->Reserved = 0;
    *pcReturned = 1;
    return TRUE;
}

static BOOL OpenPortCommon(LPWSTR pName, LPWSTR pPrinterName, PHANDLE pHandle)
{
    PPORT_CONTEXT portContext;

    if (!StringsEqualInsensitive(pName, PRINT_RXER_PORT_NAME))
    {
        SetLastError(ERROR_UNKNOWN_PORT);
        return FALSE;
    }

    portContext = (PPORT_CONTEXT)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PORT_CONTEXT));
    if (portContext == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        return FALSE;
    }

    InitializeCriticalSection(&portContext->Lock);
    portContext->LockInitialized = TRUE;
    portContext->PayloadHandle = INVALID_HANDLE_VALUE;
    CopyWideString(portContext->PortName, ARRAYSIZE(portContext->PortName), PRINT_RXER_PORT_NAME);
    CopyWideString(portContext->PrinterName, ARRAYSIZE(portContext->PrinterName), pPrinterName == NULL ? L"" : pPrinterName);
    *pHandle = (HANDLE)portContext;
    return TRUE;
}

static BOOL WINAPI MonitorEnumPorts(HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE pPorts, DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(pName);

    if (pcbNeeded == NULL || pcReturned == NULL)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    switch (Level)
    {
    case 1:
        return PackPortInfoLevel1(pPorts, cbBuf, pcbNeeded, pcReturned);
    case 2:
        return PackPortInfoLevel2(pPorts, cbBuf, pcbNeeded, pcReturned);
    default:
        SetLastError(ERROR_INVALID_LEVEL);
        return FALSE;
    }
}

static BOOL WINAPI MonitorOpenPort(HANDLE hMonitor, LPWSTR pName, PHANDLE pHandle)
{
    UNREFERENCED_PARAMETER(hMonitor);
    return OpenPortCommon(pName, NULL, pHandle);
}

static BOOL WINAPI MonitorOpenPortEx(HANDLE hMonitor, HANDLE hMonitorPort, LPWSTR pPortName, LPWSTR pPrinterName, PHANDLE pHandle, MONITOR2 *pMonitor2)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(hMonitorPort);
    UNREFERENCED_PARAMETER(pMonitor2);
    return OpenPortCommon(pPortName, pPrinterName, pHandle);
}

static BOOL WINAPI MonitorStartDocPort(HANDLE hPort, LPWSTR pPrinterName, DWORD JobId, DWORD Level, LPBYTE pDocInfo)
{
    PPORT_CONTEXT portContext;

    if (hPort == NULL)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    portContext = (PPORT_CONTEXT)hPort;
    EnterCriticalSection(&portContext->Lock);

    CleanupStagingDirectory(portContext);
    ResetPortContextState(portContext);

    portContext->JobId = JobId;
    if (!CopyWideString(portContext->PrinterName, ARRAYSIZE(portContext->PrinterName), pPrinterName == NULL ? L"" : pPrinterName))
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    if (!CopyWideString(portContext->DocumentName, ARRAYSIZE(portContext->DocumentName), GetDocumentNameFromDocInfo(Level, pDocInfo)))
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    QuerySubmittingUserForJob(
        portContext->PrinterName,
        JobId,
        portContext->SubmittingUser,
        ARRAYSIZE(portContext->SubmittingUser),
        portContext->SubmittingUserSid,
        ARRAYSIZE(portContext->SubmittingUserSid));

    if (!BuildJobPaths(portContext, JobId))
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    if (!EnsureMinimumFreeSpace(portContext->StagingDirectory, 65536ULL))
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    /* Captured payloads should also land in a newly created staging file rather
       than overwriting an existing entry. */
    portContext->PayloadHandle = CreateFileW(
        portContext->PayloadPath,
        GENERIC_WRITE,
        FILE_SHARE_READ,
        NULL,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (portContext->PayloadHandle == INVALID_HANDLE_VALUE)
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    if (!WriteMetadataFile(portContext))
    {
        CleanupStagingDirectory(portContext);
        ResetPortContextState(portContext);
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    portContext->JobStarted = TRUE;
    LeaveCriticalSection(&portContext->Lock);
    return TRUE;
}

static BOOL WINAPI MonitorWritePort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten)
{
    PPORT_CONTEXT portContext;
    BOOL success;

    if (hPort == NULL || pcbWritten == NULL)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    *pcbWritten = 0;
    portContext = (PPORT_CONTEXT)hPort;

    EnterCriticalSection(&portContext->Lock);
    if (!portContext->JobStarted || portContext->PayloadHandle == INVALID_HANDLE_VALUE)
    {
        LeaveCriticalSection(&portContext->Lock);
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    if (!EnsureMinimumFreeSpace(portContext->StagingDirectory, (ULONGLONG)cbBuf))
    {
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    success = WriteFullFile(portContext->PayloadHandle, pBuffer, cbBuf);
    if (success)
    {
        *pcbWritten = cbBuf;
        portContext->SawData = portContext->SawData || cbBuf > 0;
    }

    LeaveCriticalSection(&portContext->Lock);
    return success;
}

static BOOL WINAPI MonitorReadPort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuffer, LPDWORD pcbRead)
{
    UNREFERENCED_PARAMETER(hPort);
    UNREFERENCED_PARAMETER(pBuffer);
    UNREFERENCED_PARAMETER(cbBuffer);

    if (pcbRead != NULL)
    {
        *pcbRead = 0;
    }

    SetLastError(ERROR_NOT_SUPPORTED);
    return FALSE;
}

static BOOL WINAPI MonitorEndDocPort(HANDLE hPort)
{
    PPORT_CONTEXT portContext;
    BOOL success;

    if (hPort == NULL)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    portContext = (PPORT_CONTEXT)hPort;
    EnterCriticalSection(&portContext->Lock);

    if (!portContext->JobStarted)
    {
        LeaveCriticalSection(&portContext->Lock);
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    if (portContext->PayloadHandle != INVALID_HANDLE_VALUE)
    {
        FlushFileBuffers(portContext->PayloadHandle);
        CloseHandle(portContext->PayloadHandle);
        portContext->PayloadHandle = INVALID_HANDLE_VALUE;
    }

    if (!portContext->SawData)
    {
        CleanupStagingDirectory(portContext);
        ResetPortContextState(portContext);
        LeaveCriticalSection(&portContext->Lock);
        SetLastError(ERROR_WRITE_FAULT);
        return FALSE;
    }

    success = MoveFileExW(portContext->StagingDirectory, portContext->FinalDirectory, MOVEFILE_COPY_ALLOWED);
    if (!success)
    {
        CleanupStagingDirectory(portContext);
        ResetPortContextState(portContext);
        LeaveCriticalSection(&portContext->Lock);
        return FALSE;
    }

    portContext->JobCommitted = TRUE;
    TryDeleteCommittedPrintJob(portContext->PrinterName, portContext->JobId);
    LeaveCriticalSection(&portContext->Lock);
    return TRUE;
}

static BOOL WINAPI MonitorClosePort(HANDLE hPort)
{
    PPORT_CONTEXT portContext;

    if (hPort == NULL)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    portContext = (PPORT_CONTEXT)hPort;
    EnterCriticalSection(&portContext->Lock);
    if (portContext->JobCommitted)
    {
        TryDeleteCommittedPrintJob(portContext->PrinterName, portContext->JobId);
    }

    if (!portContext->JobCommitted)
    {
        CleanupStagingDirectory(portContext);
    }

    ResetPortContextState(portContext);
    LeaveCriticalSection(&portContext->Lock);

    if (portContext->LockInitialized)
    {
        DeleteCriticalSection(&portContext->Lock);
    }

    HeapFree(GetProcessHeap(), 0, portContext);
    return TRUE;
}

static BOOL WINAPI MonitorAddPort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pMonitorName)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(pName);
    UNREFERENCED_PARAMETER(hWnd);
    UNREFERENCED_PARAMETER(pMonitorName);

    SetLastError(ERROR_NOT_SUPPORTED);
    return FALSE;
}

static BOOL WINAPI MonitorAddPortEx(HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE lpBuffer, LPWSTR lpMonitorName)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(pName);
    UNREFERENCED_PARAMETER(Level);
    UNREFERENCED_PARAMETER(lpBuffer);
    UNREFERENCED_PARAMETER(lpMonitorName);

    return TRUE;
}

static BOOL WINAPI MonitorConfigurePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(pName);
    UNREFERENCED_PARAMETER(hWnd);
    UNREFERENCED_PARAMETER(pPortName);

    SetLastError(ERROR_NOT_SUPPORTED);
    return FALSE;
}

static BOOL WINAPI MonitorDeletePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(pName);
    UNREFERENCED_PARAMETER(hWnd);
    UNREFERENCED_PARAMETER(pPortName);

    return TRUE;
}

static BOOL WINAPI MonitorGetPrinterDataFromPort(HANDLE hPort, DWORD ControlId, LPWSTR pValueName, LPWSTR lpInBuffer, DWORD cbInBuffer, LPWSTR lpOutBuffer, DWORD cbOutBuffer, LPDWORD lpcbReturned)
{
    UNREFERENCED_PARAMETER(hPort);
    UNREFERENCED_PARAMETER(ControlId);
    UNREFERENCED_PARAMETER(pValueName);
    UNREFERENCED_PARAMETER(lpInBuffer);
    UNREFERENCED_PARAMETER(cbInBuffer);
    UNREFERENCED_PARAMETER(lpOutBuffer);
    UNREFERENCED_PARAMETER(cbOutBuffer);

    if (lpcbReturned != NULL)
    {
        *lpcbReturned = 0;
    }

    SetLastError(ERROR_NOT_SUPPORTED);
    return FALSE;
}

static BOOL WINAPI MonitorSetPortTimeouts(HANDLE hPort, LPCOMMTIMEOUTS lpCTO, DWORD reserved)
{
    UNREFERENCED_PARAMETER(hPort);
    UNREFERENCED_PARAMETER(lpCTO);
    UNREFERENCED_PARAMETER(reserved);
    return TRUE;
}

static BOOL WINAPI MonitorXcvOpenPort(HANDLE hMonitor, LPCWSTR pszObject, ACCESS_MASK GrantedAccess, PHANDLE phXcv)
{
    PXCV_CONTEXT xcvContext;

    UNREFERENCED_PARAMETER(hMonitor);

    if (phXcv == NULL)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    xcvContext = (PXCV_CONTEXT)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(XCV_CONTEXT));
    if (xcvContext == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        return FALSE;
    }

    xcvContext->GrantedAccess = GrantedAccess;
    CopyWideString(xcvContext->ObjectName, ARRAYSIZE(xcvContext->ObjectName), pszObject == NULL ? L"" : pszObject);
    *phXcv = (HANDLE)xcvContext;
    return TRUE;
}

static DWORD WINAPI MonitorXcvDataPort(HANDLE hXcv, LPCWSTR pszDataName, PBYTE pInputData, DWORD cbInputData, PBYTE pOutputData, DWORD cbOutputData, PDWORD pcbOutputNeeded)
{
    UNREFERENCED_PARAMETER(hXcv);
    UNREFERENCED_PARAMETER(pInputData);
    UNREFERENCED_PARAMETER(cbInputData);
    UNREFERENCED_PARAMETER(pOutputData);
    UNREFERENCED_PARAMETER(cbOutputData);

    if (pcbOutputNeeded != NULL)
    {
        *pcbOutputNeeded = 0;
    }

    if (pszDataName == NULL)
    {
        return ERROR_INVALID_PARAMETER;
    }

    if (StringsEqualInsensitive(pszDataName, L"AddPort") ||
        StringsEqualInsensitive(pszDataName, L"DeletePort") ||
        StringsEqualInsensitive(pszDataName, L"PortExists"))
    {
        return ERROR_SUCCESS;
    }

    return ERROR_NOT_SUPPORTED;
}

static BOOL WINAPI MonitorXcvClosePort(HANDLE hXcv)
{
    if (hXcv != NULL)
    {
        HeapFree(GetProcessHeap(), 0, hXcv);
    }

    return TRUE;
}

static VOID WINAPI MonitorShutdown(HANDLE hMonitor)
{
    if (hMonitor != NULL)
    {
        HeapFree(GetProcessHeap(), 0, hMonitor);
    }
}

static DWORD WINAPI MonitorSendRecvBidiDataFromPort(HANDLE hPort, DWORD dwAccessBit, LPCWSTR pAction, PBIDI_REQUEST_CONTAINER pReqData, PBIDI_RESPONSE_CONTAINER *ppResData)
{
    UNREFERENCED_PARAMETER(hPort);
    UNREFERENCED_PARAMETER(dwAccessBit);
    UNREFERENCED_PARAMETER(pAction);
    UNREFERENCED_PARAMETER(pReqData);
    UNREFERENCED_PARAMETER(ppResData);
    return ERROR_NOT_SUPPORTED;
}

static DWORD WINAPI MonitorNotifyUsedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR *ppszPorts)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(cPorts);
    UNREFERENCED_PARAMETER(ppszPorts);
    return ERROR_SUCCESS;
}

static DWORD WINAPI MonitorNotifyUnusedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR *ppszPorts)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(cPorts);
    UNREFERENCED_PARAMETER(ppszPorts);
    return ERROR_SUCCESS;
}

static DWORD WINAPI MonitorPowerEvent(HANDLE hMonitor, DWORD event, POWERBROADCAST_SETTING *pSettings)
{
    UNREFERENCED_PARAMETER(hMonitor);
    UNREFERENCED_PARAMETER(event);
    UNREFERENCED_PARAMETER(pSettings);
    return ERROR_SUCCESS;
}

LPMONITOR2 WINAPI InitializePrintMonitor2(PMONITORINIT pMonitorInit, PHANDLE phMonitor)
{
    PMONITOR_CONTEXT monitorContext;

    UNREFERENCED_PARAMETER(pMonitorInit);

    if (phMonitor == NULL)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return NULL;
    }

    monitorContext = (PMONITOR_CONTEXT)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(MONITOR_CONTEXT));
    if (monitorContext == NULL)
    {
        SetLastError(ERROR_OUTOFMEMORY);
        return NULL;
    }

    monitorContext->Monitor2.cbSize = sizeof(MONITOR2);
    monitorContext->Monitor2.pfnEnumPorts = MonitorEnumPorts;
    monitorContext->Monitor2.pfnOpenPort = MonitorOpenPort;
    monitorContext->Monitor2.pfnOpenPortEx = MonitorOpenPortEx;
    monitorContext->Monitor2.pfnStartDocPort = MonitorStartDocPort;
    monitorContext->Monitor2.pfnWritePort = MonitorWritePort;
    monitorContext->Monitor2.pfnReadPort = MonitorReadPort;
    monitorContext->Monitor2.pfnEndDocPort = MonitorEndDocPort;
    monitorContext->Monitor2.pfnClosePort = MonitorClosePort;
    monitorContext->Monitor2.pfnAddPort = MonitorAddPort;
    monitorContext->Monitor2.pfnAddPortEx = MonitorAddPortEx;
    monitorContext->Monitor2.pfnConfigurePort = MonitorConfigurePort;
    monitorContext->Monitor2.pfnDeletePort = MonitorDeletePort;
    monitorContext->Monitor2.pfnGetPrinterDataFromPort = MonitorGetPrinterDataFromPort;
    monitorContext->Monitor2.pfnSetPortTimeOuts = MonitorSetPortTimeouts;
    monitorContext->Monitor2.pfnXcvOpenPort = MonitorXcvOpenPort;
    monitorContext->Monitor2.pfnXcvDataPort = MonitorXcvDataPort;
    monitorContext->Monitor2.pfnXcvClosePort = MonitorXcvClosePort;
    monitorContext->Monitor2.pfnShutdown = MonitorShutdown;
    monitorContext->Monitor2.pfnSendRecvBidiDataFromPort = MonitorSendRecvBidiDataFromPort;
    monitorContext->Monitor2.pfnNotifyUsedPorts = MonitorNotifyUsedPorts;
    monitorContext->Monitor2.pfnNotifyUnusedPorts = MonitorNotifyUnusedPorts;
    monitorContext->Monitor2.pfnPowerEvent = MonitorPowerEvent;

    *phMonitor = (HANDLE)monitorContext;
    return &monitorContext->Monitor2;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(reason);
    UNREFERENCED_PARAMETER(reserved);
    return TRUE;
}
