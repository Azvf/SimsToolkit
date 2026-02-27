using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Requests;

public interface ISimsExecutionInput
{
    SimsAction Action { get; }
    string ScriptPath { get; }
    bool WhatIf { get; }
}
