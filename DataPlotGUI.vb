﻿Imports RegMapClasses
Imports System.Timers
Imports System.Windows.Forms.DataVisualization.Charting
Imports System.Threading

Public Class DataPlotGUI
    Inherits FormBase

    Private samplePeriodMs As Integer
    Private plotting As Boolean
    Private selectedRegList As List(Of RegOffsetPair)
    Private plotTimer As System.Timers.Timer
    Private plotXPosition As Integer
    Private plotYMin As Integer
    Private plotYMax As Integer
    Private plotColors As List(Of Color)
    Private numSamples As UInteger
    Private log As Boolean
    Private logData As List(Of String)
    Private logTimer As Stopwatch
    Private playBackRunning As Boolean
    Private playBackMutex As Mutex
    Private CSVRegData As List(Of String())

    Public Sub FormSetup() Handles Me.Load
        PopulateRegView()

        'Set defaults
        plotting = False
        samplePeriodMs = 100
        selectedRegList = New List(Of RegOffsetPair)
        sampleFreq.Text = "20"

        'Set color list
        plotColors = New List(Of Color)

        'Set up timer
        plotTimer = New System.Timers.Timer(500)
        plotTimer.Enabled = False
        AddHandler plotTimer.Elapsed, New ElapsedEventHandler(AddressOf PlotTimerCallback)

        samplesRendered.Text = "200"

        logTimer = New Stopwatch()

        stopPlayback.Enabled = False
        stopPlayback.Visible = False
        axis_autoscale.Checked = True

        playBackMutex = New Mutex()
    End Sub

    Private Sub ResizeHandler() Handles Me.Resize
        regView.Height = Me.Height - 157
        dataPlot.Top = 6
        dataPlot.Left = 511
        dataPlot.Width = Me.Width - 528
        dataPlot.Height = Me.Height - 55
        dataPlot.ResetAutoValues()
    End Sub

    Private Sub ShutDown() Handles Me.Closing
        plotTimer.Enabled = False
        playBackRunning = False
        playBackMutex.WaitOne()
    End Sub

    Private Sub PlotTimerCallback()
        Me.BeginInvoke(New MethodInvoker(AddressOf PlotWork))
    End Sub

    Private Sub PlotWork()
        Dim regValues() As Double
        Dim plotValues As New List(Of Double)
        Dim logStr As String = ""

        regValues = GetPlotRegValues()

        If log Then
            logStr = logTimer.ElapsedMilliseconds().ToString()
        End If

        'Update reg view and scale plot values
        Dim index As Integer = 0
        For Each item In selectedRegList
            regView.Item("Contents", item.Index).Value = regValues(index).ToString()
            regView.Item("Contents", item.Index).Style = New DataGridViewCellStyle With {.BackColor = item.Color}
            plotValues.Add(regValues(index) - item.Offset)
            'Log if needed
            If log Then
                logStr = logStr + "," + regValues(index).ToString()
            End If
            index += 1
        Next

        If log Then
            logData.Add(logStr)
        End If

        'Update the series for the plot area
        For i As Integer = 0 To selectedRegList.Count() - 1
            'remove leading point if it exists
            If dataPlot.Series(i).Points.Count() = numSamples Then
                dataPlot.Series(i).Points.RemoveAt(0)
                dataPlot.ResetAutoValues()
            End If
            dataPlot.Series(i).Points.AddXY(plotXPosition, plotValues(i))
        Next

        'Set scale (if needed)
        Dim yMin, yMax As Double
        Dim goodscale As Boolean = False
        If Not axis_autoscale.Checked Then
            Try
                yMin = Convert.ToDouble(minScale.Text())
                yMax = Convert.ToDouble(maxscale.Text())
                goodscale = True
            Catch ex As Exception
                goodscale = False
            End Try
        End If

        If goodscale Then
            'Value must be good
            dataPlot.ChartAreas(0).AxisY.Maximum = yMax
            dataPlot.ChartAreas(0).AxisY.Minimum = yMin
        End If

        plotXPosition = plotXPosition + 1

    End Sub

    Private Function GetPlotRegValues() As Double()
        'Read the registers
        If playBackRunning Then
            Dim doubleVals As New List(Of Double)
            If CSVRegData.Count() > 0 Then
                For i As Integer = 1 To CSVRegData(0).Count() - 1
                    doubleVals.Add(Convert.ToDouble(CSVRegData(0)(i)))
                Next
                CSVRegData.RemoveAt(0)
            End If
            Return doubleVals.ToArray()
        ElseIf plotting Then
            Dim regs As New List(Of RegClass)
            For Each reg In selectedRegList
                regs.Add(reg.Reg)
            Next
            Return m_TopGUI.Dut.ReadScaledValue(regs)
        Else
            'return 0
            Dim doubles(selectedRegList.Count() - 1) As Double
            Return doubles
        End If

    End Function

    Private Sub PopulateRegView()
        Dim regIndex As Integer = 0
        Dim regStr() As String
        Dim readStr As String = "Not Read"
        For Each reg In m_TopGUI.RegMap
            If reg.IsReadable Then
                If regIndex >= regView.RowCount Then
                    regStr = {reg.Label, reg.Page.ToString(), reg.Address.ToString(), readStr, "False", "0"}
                    regView.Rows.Add(regStr)
                Else
                    regView.Item("Label", regIndex).Value = reg.Label
                    regView.Item("Page", regIndex).Value = reg.Page
                    regView.Item("Address", regIndex).Value = reg.Address
                    regView.Item("Contents", regIndex).Value = readStr
                    regView.Item("Plot", regIndex).Value = True
                    regView.Item("Offset", regIndex).Value = "0"
                End If
                regIndex += 1
            End If
        Next
    End Sub

    Private Sub btn_startStop_Click(sender As Object, e As EventArgs) Handles btn_startStop.Click
        If plotting Then
            'Stop
            plotting = False
            plotTimer.Enabled = False
            StopPlot()
            If log Then
                saveCSV("PLOT_LOG", logData.ToArray(), m_TopGUI.lastFilePath)
                logData.Clear()
            End If
            btn_startStop.Text = "Start Plotting"
        Else
            log = logToCSV.Checked
            BuildPlotRegList()
            If selectedRegList.Count() = 0 Then
                MsgBox("ERROR: Must select at least one register to plot")
                Exit Sub
            End If
            plotting = True
            ConfigurePlot()
            plotTimer.Interval = samplePeriodMs
            plotTimer.Enabled = True
            btn_startStop.Text = "Stop Plotting"
            logTimer.Restart()
        End If
    End Sub

    Private Sub BuildPlotRegList()
        Dim headers As String
        For index As Integer = 0 To regView.RowCount() - 1
            If regView.Item("Plot", index).Value = True Then
                If plotColors.Count() <= selectedRegList.Count() Then
                    plotColors.Add(Color.FromArgb(CByte(Math.Floor(Rnd() * &HFF)), CByte(Math.Floor(Rnd() * &HFF)), CByte(Math.Floor(Rnd() * &HFF))))
                End If
                selectedRegList.Add(New RegOffsetPair With {.Reg = m_TopGUI.RegMap(regView.Item("Label", index).Value), .Offset = Convert.ToDouble(regView.Item("Offset", index).Value), .Index = index, .Color = plotColors(selectedRegList.Count())})
            End If
        Next
        logData = New List(Of String)
        headers = "TIMESTAMP_MS"
        For Each reg In selectedRegList
            headers = headers + "," + reg.Reg.Label
        Next
        logData.Add(headers)
    End Sub

    Private Sub ConfigurePlot()
        'Set up frequency
        Dim freq As Double
        Try
            freq = Convert.ToDouble(sampleFreq.Text)
            samplePeriodMs = 1000 / freq
            If samplePeriodMs < 5 Then
                Throw New Exception("Cannot run at more than 200Hz")
            End If
        Catch ex As Exception
            MsgBox("ERROR: Invalid sample frequency. " + ex.ToString())
            sampleFreq.Text = "10"
            samplePeriodMs = 100
        End Try

        Try
            numSamples = Convert.ToInt32(samplesRendered.Text)
        Catch ex As Exception
            MsgBox("Invalid number of samples")
            samplesRendered.Text = "500"
            ConfigurePlot()
            Exit Sub
        End Try

        'Reset the chart area
        dataPlot.ChartAreas.Clear()
        dataPlot.ChartAreas.Add(New ChartArea)

        'configure chart
        dataPlot.ChartAreas(0).AxisY.MajorGrid.Enabled = True
        dataPlot.ChartAreas(0).AxisX.MajorGrid.Enabled = True
        dataPlot.ChartAreas(0).AxisX.Title = "Sample Number"
        dataPlot.ChartAreas(0).AxisY.Title = "Scaled Value"

        'Set plotter position
        plotXPosition = 0

        'Remove all existing series
        dataPlot.Series.Clear()

        'Add series for each register
        Dim temp As Series
        For Each reg In selectedRegList
            temp = New Series
            temp.ChartType = SeriesChartType.Line
            temp.Color = reg.Color
            temp.BorderWidth = 2
            temp.Name = reg.Reg.Label
            dataPlot.Series.Add(temp)
        Next

    End Sub

    Private Sub StopPlot()
        'Reset the colors
        For Each item In selectedRegList
            regView.Item("Contents", item.Index).Style = New DataGridViewCellStyle With {.BackColor = Color.White}
        Next
        selectedRegList.Clear()
    End Sub

    Private Sub btn_autonull_Click(sender As Object, e As EventArgs) Handles btn_autonull.Click
        Dim regValues() As Double
        Dim plotValues As New List(Of Double)

        If selectedRegList.Count() = 0 Then
            Exit Sub
        End If

        'Read the registers
        regValues = GetPlotRegValues()

        For i As Integer = 0 To selectedRegList.Count() - 1
            selectedRegList(i).Offset = regValues(i)
            regView.Item("Offset", selectedRegList(i).Index).Value = regValues(i).ToString()
        Next

    End Sub

    Private Sub saveChart_Click(sender As Object, e As EventArgs) Handles saveChart.Click
        Dim filebrowser As New SaveFileDialog
        Try
            filebrowser.FileName = m_TopGUI.lastFilePath.Substring(0, m_TopGUI.lastFilePath.LastIndexOf("\") + 1) + "PLOT.png"
            filebrowser.Filter = "Image Files (*.png) | *.png"
        Catch ex As Exception
            filebrowser.FileName = "C:\PLOT.png"
        End Try

        If filebrowser.ShowDialog() = DialogResult.OK Then
            m_TopGUI.lastFilePath = filebrowser.FileName
            dataPlot.SaveImage(filebrowser.FileName, ChartImageFormat.Png)
        Else
            Exit Sub
        End If
    End Sub

    Private Sub playFromCSV_Click(sender As Object, e As EventArgs) Handles playFromCSV.Click
        Dim fileBrowser As New OpenFileDialog
        Dim fileBrowseResult As DialogResult
        Dim filePath As String
        fileBrowser.Title = "Please Select the CSV log File"
        fileBrowser.InitialDirectory = m_TopGUI.lastFilePath
        fileBrowser.Filter = "Log Files|*.csv"
        fileBrowseResult = fileBrowser.ShowDialog()
        If fileBrowseResult = DialogResult.OK Then
            filePath = fileBrowser.FileName
            CSVRegData = LoadFromCSV(filePath)
        Else
            Exit Sub
        End If

        If Not SetupCSVRegs() Then
            MsgBox("ERROR: Invalid Log CSV")
            Exit Sub
        End If
        BuildPlotRegList()
        ConfigurePlot()
        DisablePlaybackButtons()
        playBackRunning = True

        Dim temp As New Thread(AddressOf PlayCSVWorker)
        temp.Start()

    End Sub

    Private Function SetupCSVRegs() As Boolean
        Dim headers() As String
        Dim regFound As Boolean
        Dim regCnt As Integer
        selectedRegList.Clear()
        If CSVRegData.Count() > 0 Then
            headers = CSVRegData(0)
            CSVRegData.RemoveAt(0)
        Else
            Return False
        End If
        'CHeck that timestamp values are included
        If Not headers(0) = "TIMESTAMP_MS" Then
            Return False
        End If
        'Check each box in the reg list
        regCnt = 0
        For j As Integer = 0 To regView.RowCount() - 1
            regFound = headers.Contains(regView.Item("Label", j).Value)
            regView.Item("Plot", j).Value = regFound
            If regFound Then
                regCnt += 1
            End If
        Next
        Return regCnt = headers.Count() - 1
    End Function

    Private Sub PlayCSVWorker()
        playBackMutex.WaitOne()
        Dim waitTime As Long
        logTimer.Restart()

        While CSVRegData.Count() > 0 And playBackRunning
            waitTime = Convert.ToDouble(CSVRegData(0)(0))
            While logTimer.ElapsedMilliseconds() < waitTime And playBackRunning
                System.Threading.Thread.Sleep(1)
            End While
            If Not playBackRunning Then
                Exit While
            End If
            Me.Invoke(New MethodInvoker(AddressOf PlotWork))
        End While

        Me.Invoke(New MethodInvoker(AddressOf EnablePlaybackButtons))
        playBackMutex.ReleaseMutex()
    End Sub

    Private Function LoadFromCSV(fileName As String) As List(Of String())
        Dim reader As New IO.StreamReader(fileName)
        Dim result As New List(Of String())
        Dim line As String
        Dim lineValues() As String

        While Not reader.EndOfStream()
            line = reader.ReadLine()
            lineValues = line.Split(",")
            result.Add(lineValues)
        End While

        Return result
    End Function

    Private Sub stopPlayback_Click(sender As Object, e As EventArgs) Handles stopPlayback.Click
        playBackRunning = False
    End Sub

    Private Sub EnablePlaybackButtons()
        playFromCSV.Visible = True
        playFromCSV.Enabled = True
        stopPlayback.Visible = False
        stopPlayback.Enabled = False
        btn_startStop.Enabled = True
    End Sub

    Private Sub DisablePlaybackButtons()
        playFromCSV.Visible = False
        playFromCSV.Enabled = False
        stopPlayback.Visible = True
        stopPlayback.Enabled = True
        btn_startStop.Enabled = False
    End Sub

    Private Sub axis_autoscale_CheckedChanged(sender As Object, e As EventArgs) Handles axis_autoscale.CheckedChanged
        If axis_autoscale.Checked Then
            minScale.Enabled = False
            maxscale.Enabled = False
            dataPlot.ChartAreas(0).AxisY.Minimum = Double.NaN
            dataPlot.ChartAreas(0).AxisY.Maximum = Double.NaN
        Else
            minScale.Enabled = True
            maxscale.Enabled = True
            Try
                minScale.Text = dataPlot.ChartAreas(0).AxisY.Minimum.ToString()
                maxscale.Text = dataPlot.ChartAreas(0).AxisY.Maximum.ToString()
            Catch ex As Exception
                minScale.Text = "-1000"
                maxscale.Text = "1000"
            End Try
        End If
    End Sub
End Class