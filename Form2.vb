Imports System.Runtime.InteropServices

Public Class Form2

    Dim filteredPids As HashSet(Of Integer)
    Dim nombreFiltro As String
    Private Function ListarMutexesv3(mutantType As Byte) As List(Of String)

        Dim resultado As New List(Of String)
        Dim buffer As IntPtr = IntPtr.Zero

        Try
            buffer = GetSystemHandleInformationBuffer()
            If buffer = IntPtr.Zero Then Return resultado

            Dim handleCount As UInteger = CUInt(Marshal.ReadInt32(buffer))
            Dim ptr As IntPtr = buffer + 4
            Dim size As Integer = Marshal.SizeOf(GetType(SYSTEM_HANDLE))

            For i As UInteger = 0 To handleCount - 1

                Dim entry As SYSTEM_HANDLE =
                Marshal.PtrToStructure(Of SYSTEM_HANDLE)(ptr)

                If filteredPids.Contains(entry.ProcessId) AndAlso
               entry.ObjectTypeNumber = mutantType Then

                    Dim nombre As String = GetObjectName(entry)

                    If Not String.IsNullOrEmpty(nombre) Then

                        ' Extraer solo el nombre final
                        Dim lastSlash As Integer = nombre.LastIndexOf("\"c)
                        If lastSlash >= 0 Then
                            nombre = nombre.Substring(lastSlash + 1)
                        End If

                        ' Filtro por nombre si se especifica
                        If String.IsNullOrWhiteSpace(nombreFiltro) OrElse
                       nombre.IndexOf(nombreFiltro, StringComparison.OrdinalIgnoreCase) >= 0 Then
                            'resultado.Add($"{nombre} (PID={entry.ProcessId}, Handle={entry.Handle})")
                            resultado.Add($"{nombre}")
                        End If
                    End If
                End If

                ptr += size
            Next

        Finally
            If buffer <> IntPtr.Zero Then
                Marshal.FreeHGlobal(buffer)
            End If
        End Try

        Return resultado

    End Function

    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        ListBox1.Items.Clear()

        Dim filtroProceso = TextBox1.Text.ToLower()
        nombreFiltro = ""
        'Dim filtroMutex = TextBox2.Text ' si agregas un textbox para nombre mutex

        Dim lista = Await Task.Run(Function()

                                       ' Obtener tipo
                                       Dim tipo As Byte = GetMutantObjectType()
                                       If tipo = 0 Then Return Nothing

                                       ' Construir hashset sin Invoke
                                       If String.IsNullOrWhiteSpace(filtroProceso) Then
                                           filteredPids = Process.GetProcesses().
                Select(Function(p) p.Id).
                ToHashSet()
                                       Else
                                           filteredPids = Process.GetProcesses().
                Where(Function(p)
                          Try
                              Return p.ProcessName.ToLower().StartsWith(filtroProceso)
                          Catch
                              Return False
                          End Try
                      End Function).
                Select(Function(p) p.Id).
                ToHashSet()
                                       End If

                                       Return ListarMutexesv3(tipo)

                                   End Function)

        ListBox1.Items.Clear()

        If lista Is Nothing Then
            'ListBox1.Items.Add("No se pudo obtener el tipo Mutant.")
            Return
        End If

        For Each item In lista
            ListBox1.Items.Add(item)
        Next
    End Sub

    <StructLayout(LayoutKind.Sequential)>
    Public Structure SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
        Public ObjectPTR As IntPtr
        Public UniqueProcessId As IntPtr
        Public HandleValue As IntPtr
        Public GrantedAccess As UInteger
        Public CreatorBackTraceIndex As UShort
        Public ObjectTypeIndex As UShort
        Public HandleAttributes As UInteger
        Public Reserved As UInteger
    End Structure

    Private Const SystemExtendedHandleInformation As Integer = 64
    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        ListBox1.Items.Clear()

        ' Obtener PID por nombre
        Dim procesos = Process.GetProcessesByName(TextBox1.Text)

        If procesos.Length = 0 Then
            Exit Sub
        End If

        Dim pid As Integer =
        procesos.OrderByDescending(Function(p) p.StartTime).
        First().Id

        'Obtener tipo Mutant
        Dim tipo As Byte = GetMutantObjectType()
        If tipo = 0 Then
            Exit Sub
        End If

        Dim length As Integer = 0
        Dim infoPtr As IntPtr = IntPtr.Zero
        Dim status As Integer

        'Obtener tamaño requerido
        NtQuerySystemInformation(SystemExtendedHandleInformation,
                             IntPtr.Zero,
                             0,
                             length)

        If length = 0 Then
            Exit Sub
        End If

        'Obtener buffer
        Do
            infoPtr = Marshal.AllocHGlobal(length)

            status = NtQuerySystemInformation(SystemExtendedHandleInformation,
                                          infoPtr,
                                          length,
                                          length)

            If status = &HC0000004 Then
                Marshal.FreeHGlobal(infoPtr)
                infoPtr = IntPtr.Zero
            Else
                Exit Do
            End If

        Loop While status = &HC0000004

        If status <> 0 Then
            If infoPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(infoPtr)
            Exit Sub
        End If

        'Leer header correctamente (x64)
        Dim handleCount As Long = Marshal.ReadIntPtr(infoPtr).ToInt64()
        Dim offset As Integer = IntPtr.Size * 2
        Dim size As Integer = Marshal.SizeOf(GetType(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX))

        'Iterar handles
        For i As Long = 0 To handleCount - 1

            Dim entry As SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX =
            Marshal.PtrToStructure(Of SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)(infoPtr + offset)

            offset += size

            If entry.ObjectTypeIndex = tipo AndAlso
           entry.UniqueProcessId.ToInt32() = pid Then

                Dim sh As New SYSTEM_HANDLE
                sh.ProcessId = entry.UniqueProcessId.ToInt32()
                sh.Handle = entry.HandleValue
                sh.ObjectTypeNumber = entry.ObjectTypeIndex

                Dim nombre As String = GetObjectName(sh)

                If Not String.IsNullOrEmpty(nombre) Then

                    'Extraer solo el nombre final
                    Dim lastSlash As Integer = nombre.LastIndexOf("\"c)
                    If lastSlash >= 0 Then
                        nombre = nombre.Substring(lastSlash + 1)
                        ListBox1.Items.Add(nombre & " - " & sh.Handle & "|" & sh.ObjectTypeNumber)
                    End If
                End If
            End If
        Next
        Marshal.FreeHGlobal(infoPtr)
    End Sub

    Private Sub Form2_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub

    Private Async Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click

        ListBox1.Items.Clear()

        Dim nombreProceso As String = TextBox1.Text.Trim()

        If String.IsNullOrWhiteSpace(nombreProceso) Then
            Exit Sub
        End If

        Dim lista = Await Task.Run(Function()

                                       Dim resultado As New List(Of String)

                                       'Obtener procesos por nombre (pueden ser varios)
                                       Dim procesos = Process.GetProcessesByName(nombreProceso)

                                       If procesos.Length = 0 Then
                                           Return resultado
                                       End If

                                       Dim pids As HashSet(Of Integer) =
                                       procesos.Select(Function(p) p.Id).ToHashSet()

                                       'Obtener tipo Mutant
                                       Dim tipo As Byte = GetMutantObjectType()
                                       If tipo = 0 Then Return resultado

                                       Dim length As Integer = 0

                                       'Primer llamada para tamaño
                                       NtQuerySystemInformation(SystemExtendedHandleInformation,
                                                            IntPtr.Zero,
                                                            0,
                                                            length)

                                       If length = 0 Then Return resultado

                                       Dim infoPtr As IntPtr = IntPtr.Zero
                                       Dim status As Integer

                                       'Obtener buffer dinámico
                                       Do
                                           infoPtr = Marshal.AllocHGlobal(length)

                                           status = NtQuerySystemInformation(SystemExtendedHandleInformation,
                                                                         infoPtr,
                                                                         length,
                                                                         length)

                                           If status = &HC0000004 Then ' STATUS_INFO_LENGTH_MISMATCH
                                               Marshal.FreeHGlobal(infoPtr)
                                               infoPtr = IntPtr.Zero
                                           Else
                                               Exit Do
                                           End If

                                       Loop While status = &HC0000004

                                       If status <> 0 OrElse infoPtr = IntPtr.Zero Then
                                           If infoPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(infoPtr)
                                           Return resultado
                                       End If

                                       Try
                                           'Leer header (x64 safe)
                                           Dim handleCount As Long = Marshal.ReadIntPtr(infoPtr).ToInt64()
                                           Dim offset As Integer = IntPtr.Size * 2
                                           Dim size As Integer =
                                           Marshal.SizeOf(GetType(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX))

                                           For i As Long = 0 To handleCount - 1

                                               Dim entry As SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX =
                                               Marshal.PtrToStructure(Of SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)(infoPtr + offset)

                                               offset += size

                                               Dim pid As Integer = entry.UniqueProcessId.ToInt32()

                                               'FILTRO TEMPRANO (CLAVE DE RENDIMIENTO)
                                               If Not pids.Contains(pid) Then Continue For
                                               If entry.ObjectTypeIndex <> tipo Then Continue For

                                               Dim sh As New SYSTEM_HANDLE With {
                                               .ProcessId = pid,
                                               .Handle = entry.HandleValue,
                                               .ObjectTypeNumber = entry.ObjectTypeIndex
                                           }

                                               Dim nombre As String = GetObjectName(sh)

                                               If Not String.IsNullOrEmpty(nombre) Then
                                                   Dim lastSlash As Integer = nombre.LastIndexOf("\"c)
                                                   If lastSlash >= 0 Then
                                                       nombre = nombre.Substring(lastSlash + 1)
                                                   End If

                                                   resultado.Add(
                                                   $"{nombre} (PID={pid}, Handle=0x{sh.Handle:X})")

                                                   'Cerrar MUTEX - versión linea ultra-comprimida
                                                   Dim h = OpenProcess(&H40UI, False, CInt(pid)) : If h <> IntPtr.Zero Then Dim d As IntPtr = IntPtr.Zero : Dim s = DuplicateHandle(h, New IntPtr(sh.Handle), GetCurrentProcess(), d, 0, False, &H1UI) : If s AndAlso d <> IntPtr.Zero Then CloseHandle(d) : CloseHandle(h)

                                               End If

                                           Next

                                       Finally
                                           Marshal.FreeHGlobal(infoPtr)
                                       End Try

                                       Return resultado

                                   End Function)

        For Each item In lista
            ListBox1.Items.Add(item)
        Next

    End Sub

    Private Async Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click

        ListBox1.Items.Clear()

        Dim filtroProceso As String = TextBox1.Text.ToLower()
        nombreFiltro = "" ' si luego agregas textbox para mutex, asígnalo aquí

        Dim lista = Await Task.Run(Function()

                                       Dim resultado As New List(Of String)

                                       ' ===== Obtener tipo Mutant =====
                                       Dim tipo As Byte = GetMutantObjectType()
                                       If tipo = 0 Then Return resultado

                                       ' ===== Construir lista de PID filtrados =====
                                       Dim pids As HashSet(Of Integer)

                                       If String.IsNullOrWhiteSpace(filtroProceso) Then
                                           pids = Process.GetProcesses().
                                           Select(Function(p) p.Id).
                                           ToHashSet()
                                       Else
                                           pids = Process.GetProcesses().
                                           Where(Function(p)
                                                     Try
                                                         Return p.ProcessName.ToLower().StartsWith(filtroProceso)
                                                     Catch
                                                         Return False
                                                     End Try
                                                 End Function).
                                           Select(Function(p) p.Id).
                                           ToHashSet()
                                       End If

                                       If pids.Count = 0 Then Return resultado

                                       ' ===== Obtener tamaño requerido =====
                                       Dim length As Integer = 0
                                       NtQuerySystemInformation(SystemExtendedHandleInformation,
                                                            IntPtr.Zero,
                                                            0,
                                                            length)

                                       If length = 0 Then Return resultado

                                       Dim infoPtr As IntPtr = IntPtr.Zero
                                       Dim status As Integer

                                       ' ===== Obtener buffer dinámico =====
                                       Do
                                           infoPtr = Marshal.AllocHGlobal(length)

                                           status = NtQuerySystemInformation(SystemExtendedHandleInformation,
                                                                         infoPtr,
                                                                         length,
                                                                         length)

                                           If status = &HC0000004 Then
                                               Marshal.FreeHGlobal(infoPtr)
                                               infoPtr = IntPtr.Zero
                                           Else
                                               Exit Do
                                           End If

                                       Loop While status = &HC0000004

                                       If status <> 0 OrElse infoPtr = IntPtr.Zero Then
                                           If infoPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(infoPtr)
                                           Return resultado
                                       End If

                                       Try
                                           ' ===== Leer header (x64 safe) =====
                                           Dim handleCount As Long = Marshal.ReadIntPtr(infoPtr).ToInt64()
                                           Dim offset As Integer = IntPtr.Size * 2
                                           Dim size As Integer = Marshal.SizeOf(GetType(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX))

                                           ' ===== Iterar handles =====
                                           For i As Long = 0 To handleCount - 1

                                               Dim entry As SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX =
                                               Marshal.PtrToStructure(Of SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)(infoPtr + offset)

                                               offset += size

                                               Dim pid As Integer = entry.UniqueProcessId.ToInt32()

                                               If entry.ObjectTypeIndex = tipo AndAlso pids.Contains(pid) Then

                                                   Dim sh As New SYSTEM_HANDLE
                                                   sh.ProcessId = pid
                                                   sh.Handle = entry.HandleValue
                                                   sh.ObjectTypeNumber = entry.ObjectTypeIndex

                                                   Dim nombre As String = GetObjectName(sh)

                                                   If Not String.IsNullOrEmpty(nombre) Then

                                                       Dim lastSlash As Integer = nombre.LastIndexOf("\"c)
                                                       If lastSlash >= 0 Then
                                                           nombre = nombre.Substring(lastSlash + 1)
                                                       End If

                                                       If String.IsNullOrWhiteSpace(nombreFiltro) OrElse
                                                      nombre.IndexOf(nombreFiltro, StringComparison.OrdinalIgnoreCase) >= 0 Then

                                                           resultado.Add($"{nombre} (PID={pid}, Handle=0x{sh.Handle:X})")

                                                       End If
                                                   End If
                                               End If
                                           Next

                                       Finally
                                           Marshal.FreeHGlobal(infoPtr)
                                       End Try

                                       Return resultado

                                   End Function)

        For Each item In lista
            ListBox1.Items.Add(item)
        Next

    End Sub
End Class