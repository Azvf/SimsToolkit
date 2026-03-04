
namespace SimsModDesktop.Application.Requests;

public interface ISimsExecutionInput
{
    SimsAction Action { get; }
    bool WhatIf { get; }
}
