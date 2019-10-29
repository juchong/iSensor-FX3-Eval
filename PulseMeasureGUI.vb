﻿'File:          PulseMeasureGUI.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          7/26/2019
'Description:   Allows for precise pulse measurement with a trigger condition.

Imports AdisApi
Imports FX3Api

Public Class PulseMeasureGUI
    Inherits FormBase

    Private PinList As List(Of IPinObject)


    Public Sub FormSetup() Handles Me.Load
        'Populate pin box
        PinList = New List(Of IPinObject)
        triggerPin.DropDownStyle = ComboBoxStyle.DropDownList
        busyPin.DropDownStyle = ComboBoxStyle.DropDownList
        Dim FX3Api = GetType(FX3Connection)
        For Each prop In FX3Api.GetProperties()
            If prop.PropertyType = GetType(IPinObject) Then
                If Not prop.Name = "Reset" Then
                    triggerPin.Items.Add(prop.Name)
                    busyPin.Items.Add(prop.Name)
                    PinList.Add(m_TopGUI.FX3.GetType().GetProperty(prop.Name).GetValue(m_TopGUI.FX3))
                End If
            End If
        Next
        If triggerPin.Items.Count > 0 Then
            triggerPin.SelectedIndex = 0
            busyPin.SelectedIndex = 0
        End If
    End Sub

    Private Sub startBtn_Click(sender As Object, e As EventArgs) Handles startBtn.Click

        Dim trig As IPinObject
        Dim busy As IPinObject

        trig = PinList(triggerPin.SelectedIndex)
        busy = PinList(busyPin.SelectedIndex)

        'get the mode
        If modePages.SelectedTab.Text = "Pin Triggered" Then
            If busyPin.SelectedItem = triggerPin.SelectedItem Then
                MsgBox("ERROR: Busy pin and trigger pin cannot be the same")
                Exit Sub
            End If
            Try
                label_result.Text = m_TopGUI.FX3.MeasureBusyPulse(trig, Convert.ToInt32(driveTime.Text), Convert.ToUInt32(drivePolarity.Text), busy, Convert.ToUInt16(busyPolarity.Text, 16), Convert.ToInt32(timeout.Text))
            Catch ex As Exception
                MsgBox("ERROR: Invalid config. " + ex.Message())
            End Try
        Else
            Try
                label_result.Text = m_TopGUI.FX3.MeasureBusyPulse(Convert.ToUInt32(triggerReg.Text, 16), Convert.ToUInt32(triggerRegVal.Text, 16), busy, Convert.ToUInt32(busyPolarity.Text, 16), Convert.ToInt32(timeout.Text)).ToString()
            Catch ex As Exception
                MsgBox("ERROR: Invalid config. " + ex.Message())
            End Try
        End If

    End Sub

End Class