Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Threading

Module Mutantes

    ' Constants
    Const STATUS_SUCCESS As Integer = 0
    Const STATUS_INFO_LENGTH_MISMATCH As Integer = &HC0000004
    Const SystemHandleInformation As Integer = 16
    Const ObjectNameInformation As Integer = 1
    Const PROCESS_DUP_HANDLE As UInteger = &H40UI
    Const DUPLICATE_SAME_ACCESS As UInteger = &H2UI
    Const DUPLICATE_CLOSE_SOURCE As UInteger = &H1UI

    ' Structs
    <StructLayout(LayoutKind.Sequential)>
    Public Structure UNICODE_STRING
        Public Length As UShort
        Public MaximumLength As UShort
        Public Buffer As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure OBJECT_NAME_INFORMATION
        Public Name As UNICODE_STRING
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure SYSTEM_HANDLE
        Public ProcessId As UInteger
        Public ObjectTypeNumber As Byte
        Public Flags As Byte
        Public Handle As UShort
        Public [Object] As IntPtr
        Public GrantedAccess As UInteger
    End Structure

    ' P/Invokes
    <DllImport("ntdll.dll", SetLastError:=True)>
    Public Function NtQuerySystemInformation(ByVal SystemInformationClass As Integer, ByVal SystemInformation As IntPtr, ByVal SystemInformationLength As UInteger, ByRef ReturnLength As UInteger) As Integer
    End Function

    <DllImport("ntdll.dll", SetLastError:=True)>
    Public Function NtQueryObject(ByVal ObjectHandle As IntPtr, ByVal ObjectInformationClass As Integer, ByVal ObjectInformation As IntPtr, ByVal ObjectInformationLength As UInteger, ByRef ReturnLength As UInteger) As Integer
    End Function

    <DllImport("ntdll.dll", SetLastError:=True)>
    Public Function NtDuplicateObject(ByVal SourceProcessHandle As IntPtr, ByVal SourceHandle As IntPtr, ByVal TargetProcessHandle As IntPtr, ByRef TargetHandle As IntPtr, ByVal DesiredAccess As UInteger, ByVal Attributes As UInteger, ByVal Options As UInteger) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Function OpenProcess(ByVal dwDesiredAccess As UInteger, ByVal bInheritHandle As Boolean, ByVal dwProcessId As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Function DuplicateHandle(ByVal hSourceProcessHandle As IntPtr, ByVal hSourceHandle As IntPtr, ByVal hTargetProcessHandle As IntPtr, ByRef lpTargetHandle As IntPtr, ByVal dwDesiredAccess As UInteger, ByVal bInheritHandle As Boolean, ByVal dwOptions As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Function CloseHandle(ByVal hObject As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Public Function GetCurrentProcess() As IntPtr
    End Function


    Sub Main()

        Try
            ' Obtener el tipo de objeto para Mutex (Mutant)
            Dim mutantType As Byte = GetMutantObjectType()

            If mutantType = 0 Then
                Return
            End If

            ' Listar todos los mutex nombrados
            Dim mutexList As Dictionary(Of String, List(Of Tuple(Of UInteger, UShort))) = ListAllNamedMutexes(mutantType)

            For Each kvp In mutexList
                For Each item In kvp.Value
                Next
            Next

            Dim targetName As String = Console.ReadLine().Trim()

            If Not String.IsNullOrEmpty(targetName) Then
                CloseMutexByName(targetName, mutexList)
            End If

        Catch ex As Exception
        End Try

    End Sub

    Public Function CerrarMutexDirecto(nombreProceso As String, nombreMutex As String) As Boolean

        Dim pList = Process.GetProcessesByName(
        IO.Path.GetFileNameWithoutExtension(nombreProceso)
    )

        If pList.Length = 0 Then Return False

        For Each p In pList
            Dim hProc As IntPtr = OpenProcess(PROCESS_DUP_HANDLE, False, p.Id)

            If hProc = IntPtr.Zero Then Continue For

            Dim buffer As IntPtr = GetSystemHandleInformationBuffer()
            If buffer = IntPtr.Zero Then Continue For

            Dim count As UInteger = CUInt(Marshal.ReadInt32(buffer))
            Dim ptr As IntPtr = buffer + 4

            Dim mutantType As Byte = GetMutantObjectType()

            For i As UInteger = 0 To count - 1

                Dim entry As SYSTEM_HANDLE =
                Marshal.PtrToStructure(Of SYSTEM_HANDLE)(ptr)

                If entry.ProcessId = p.Id AndAlso entry.ObjectTypeNumber = mutantType Then

                    Dim name As String = GetObjectName(entry)

                    If Not String.IsNullOrEmpty(name) AndAlso
                    name.IndexOf(nombreMutex, StringComparison.OrdinalIgnoreCase) >= 0 Then

                        Dim dummy As IntPtr

                        Dim ok As Boolean = DuplicateHandle(
                        hProc,
                        New IntPtr(entry.Handle),
                        GetCurrentProcess(),
                        dummy,
                        0,
                        False,
                        DUPLICATE_CLOSE_SOURCE)

                        If ok Then
                            If dummy <> IntPtr.Zero Then CloseHandle(dummy)
                            Return True
                        End If

                    End If
                End If

                ptr += Marshal.SizeOf(GetType(SYSTEM_HANDLE))
            Next

            CloseHandle(hProc)
        Next

        Return False
    End Function

    Public Function CerrarHandleMutex(pid As UInteger, handleValue As UShort) As Boolean

        Dim hProcess As IntPtr = OpenProcess(PROCESS_DUP_HANDLE, False, CInt(pid))
        If hProcess = IntPtr.Zero Then Return False

        Try
            Dim dummy As IntPtr = IntPtr.Zero
            Dim success As Boolean = DuplicateHandle(hProcess, New IntPtr(handleValue), GetCurrentProcess(), dummy, 0, False, DUPLICATE_CLOSE_SOURCE)

            If success AndAlso dummy <> IntPtr.Zero Then
                CloseHandle(dummy)
            End If

            Return success

        Finally
            CloseHandle(hProcess)
        End Try

    End Function

    Public Function GetMutantObjectType() As Byte
        Dim uniqueName As String = Guid.NewGuid().ToString()
        Dim testMutex As New System.Threading.Mutex(False, uniqueName)
        Dim testHandle As IntPtr = testMutex.SafeWaitHandle.DangerousGetHandle()
        Dim testHandleValue As UShort = CUShort(testHandle.ToInt32() And &HFFFF) ' explícito
        Dim myPid As UInteger = CUInt(Process.GetCurrentProcess().Id)

        Dim buffer As IntPtr = IntPtr.Zero
        Try
            buffer = GetSystemHandleInformationBuffer()
            If buffer = IntPtr.Zero Then Return 0

            ' El primer 4 bytes es el número de handles "ULONG"
            Dim handleCount As UInteger = CUInt(Marshal.ReadInt32(buffer))
            Dim ptr As IntPtr = IntPtr.Add(buffer, 4) ' "ULONG" = 4 bytes

            Dim entrySize As Integer = Marshal.SizeOf(GetType(SYSTEM_HANDLE))
            For i As UInteger = 0 To handleCount - 1
                Dim entry As SYSTEM_HANDLE = Marshal.PtrToStructure(Of SYSTEM_HANDLE)(ptr)
                If entry.ProcessId = myPid AndAlso entry.Handle = testHandleValue Then
                    Return entry.ObjectTypeNumber
                End If
                ptr = IntPtr.Add(ptr, entrySize)
            Next
        Finally
            If buffer <> IntPtr.Zero Then Marshal.FreeHGlobal(buffer)
            Try
                testMutex.Close()
            Catch
            End Try
        End Try

        Return 0
    End Function

    Private Function ListAllNamedMutexes(ByVal mutantType As Byte) As Dictionary(Of String, List(Of Tuple(Of UInteger, UShort)))
        Dim mutexDict As New Dictionary(Of String, List(Of Tuple(Of UInteger, UShort)))(StringComparer.OrdinalIgnoreCase)

        Dim buffer As IntPtr = IntPtr.Zero
        Try
            buffer = GetSystemHandleInformationBuffer()
            If buffer = IntPtr.Zero Then Return mutexDict

            Dim handleCount As UInteger = CUInt(Marshal.ReadInt32(buffer))
            Dim ptr As IntPtr = IntPtr.Add(buffer, 4)
            Dim entrySize As Integer = Marshal.SizeOf(GetType(SYSTEM_HANDLE))

            For i As UInteger = 0 To handleCount - 1
                Dim entry As SYSTEM_HANDLE = Marshal.PtrToStructure(Of SYSTEM_HANDLE)(ptr)
                If entry.ObjectTypeNumber = mutantType AndAlso entry.ProcessId > 0 Then
                    Dim name As String = GetObjectName(entry)
                    If Not String.IsNullOrEmpty(name) Then
                        If Not mutexDict.ContainsKey(name) Then
                            mutexDict(name) = New List(Of Tuple(Of UInteger, UShort))
                        End If
                        mutexDict(name).Add(New Tuple(Of UInteger, UShort)(entry.ProcessId, entry.Handle))
                    End If
                End If
                ptr = IntPtr.Add(ptr, entrySize)
            Next
        Finally
            If buffer <> IntPtr.Zero Then Marshal.FreeHGlobal(buffer)
        End Try

        Return mutexDict
    End Function

    Public Function GetSystemHandleInformationBuffer() As IntPtr
        Dim bufferSize As UInteger = &H10000UI
        Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(bufferSize))
        Dim returnLength As UInteger = 0
        Dim status As Integer

        While True
            status = NtQuerySystemInformation(SystemHandleInformation, buffer, bufferSize, returnLength)
            If status = STATUS_SUCCESS Then
                Return buffer
            ElseIf status <> STATUS_INFO_LENGTH_MISMATCH Then
                Marshal.FreeHGlobal(buffer)
                Return IntPtr.Zero
            End If

            Marshal.FreeHGlobal(buffer)

            bufferSize = returnLength + 1024UI
            buffer = Marshal.AllocHGlobal(CInt(bufferSize))
        End While
    End Function

    'mejorado
    Public Function GetObjectName(ByVal entry As SYSTEM_HANDLE) As String

        Dim processHandle As IntPtr =
        OpenProcess(PROCESS_DUP_HANDLE, False, entry.ProcessId)

        If processHandle = IntPtr.Zero Then Return Nothing

        Try
            Dim localHandle As IntPtr = IntPtr.Zero

            Dim ntStatus As Integer =
            NtDuplicateObject(processHandle,
                              entry.Handle,
                              GetCurrentProcess(),
                              localHandle,
                              0UI,
                              0UI,
                              DUPLICATE_SAME_ACCESS)

            If ntStatus <> STATUS_SUCCESS OrElse localHandle = IntPtr.Zero Then
                Return Nothing
            End If

            Try
                Dim nameLength As UInteger = 0

                ' primera llamada para obtener tamaño
                NtQueryObject(localHandle,
                          ObjectNameInformation,
                          IntPtr.Zero,
                          0UI,
                          nameLength)

                If nameLength = 0 Then Return Nothing

                Dim nameBuffer As IntPtr =
                Marshal.AllocHGlobal(CInt(nameLength))

                Try
                    ntStatus = NtQueryObject(localHandle,
                                         ObjectNameInformation,
                                         nameBuffer,
                                         nameLength,
                                         nameLength)

                    If ntStatus <> STATUS_SUCCESS Then Return Nothing

                    Dim objNameInfo As OBJECT_NAME_INFORMATION =
                    Marshal.PtrToStructure(Of OBJECT_NAME_INFORMATION)(nameBuffer)

                    If objNameInfo.Name.Buffer = IntPtr.Zero _
                   OrElse objNameInfo.Name.Length = 0 Then
                        Return Nothing
                    End If

                    Return Marshal.PtrToStringUni(
                    objNameInfo.Name.Buffer,
                    objNameInfo.Name.Length \ 2)

                Finally
                    Marshal.FreeHGlobal(nameBuffer)
                End Try

            Finally
                CloseHandle(localHandle)
            End Try

        Finally
            CloseHandle(processHandle)
        End Try

    End Function

    Private Sub CloseMutexByName(ByVal targetName As String, ByVal mutexList As Dictionary(Of String, List(Of Tuple(Of UInteger, UShort))))
        Dim found As Boolean = False
        For Each kvp In mutexList
            If kvp.Key.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 Then
                found = True
                For Each item In kvp.Value
                    Dim pid As UInteger = item.Item1
                    Dim handleValue As UShort = item.Item2

                    Dim processHandle As IntPtr = OpenProcess(PROCESS_DUP_HANDLE, False, CInt(pid))
                    If processHandle <> IntPtr.Zero Then
                        Try
                            Dim dummyHandle As IntPtr = IntPtr.Zero
                            Dim sourceHandle As IntPtr = New IntPtr(CInt(handleValue)) ' conversión explícita
                            Dim success As Boolean = DuplicateHandle(processHandle, sourceHandle, GetCurrentProcess(), dummyHandle, 0UI, False, DUPLICATE_CLOSE_SOURCE)
                            If success Then
                                If dummyHandle <> IntPtr.Zero Then CloseHandle(dummyHandle)
                            Else
                            End If
                        Finally
                            CloseHandle(processHandle)
                        End Try
                    Else
                        Console.WriteLine("Error proceso PID: " & pid)
                    End If
                Next
            End If
        Next

        If Not found Then
        End If
    End Sub



End Module
