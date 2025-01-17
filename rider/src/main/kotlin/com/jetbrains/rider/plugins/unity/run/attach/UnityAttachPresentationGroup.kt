package com.jetbrains.rider.plugins.unity.run.attach

import com.intellij.execution.process.ProcessInfo
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.UserDataHolder
import com.intellij.xdebugger.attach.XAttachProcessPresentationGroup
import icons.UnityIcons

@Suppress("UnstableApiUsage")
object UnityAttachPresentationGroup : XAttachProcessPresentationGroup {
    override fun getProcessIcon(project: Project, process: ProcessInfo, userData: UserDataHolder) = UnityIcons.Icons.UnityLogo
    override fun getProcessDisplayText(project: Project, process: ProcessInfo, userData: UserDataHolder) = process.executableDisplayName
    override fun getOrder(): Int = 3
    override fun getGroupName(): String = "Local Unity processes"
    override fun compare(p1: ProcessInfo, p2: ProcessInfo) = p1.pid.compareTo(p2.pid)
}