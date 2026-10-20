namespace AIThread.Common.Enums;

// What a call inside a function reaches (CodeFunctionCall).
public enum CodeCallKind
{
    // A plain function: p_initiateData(), format_text($t).
    Function = 1,

    // A member of a class, object or module: clsKlaimDFBL.GetByDocNo(), $this->save(), clsGlobal.showLoading().
    Method = 2,

    // A request to an endpoint: $.ajax url, fetch, Url.Action, form action.
    Http = 3,

    // A view or template rendered or loaded: View("Index"), partials, templates.
    View = 4,

    // A stored procedure, table or SQL statement.
    Sql = 5,

    Other = 9
}

public enum FunctionScanStatus
{
    Done = 1,
    Failed = 2
}
