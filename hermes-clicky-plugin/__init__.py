"""hermes-clicky-plugin registration."""

from . import macos_capabilities, schemas, tools


def register(ctx):
    """Register Clicky tools with Hermes.

    Hermes loads plugin directories and calls register(ctx). Keep registration
    boring: schemas live in schemas.py, handlers live in tools.py.
    """
    ctx.register_tool(
        name="get_clicky_capabilities",
        toolset="clicky",
        schema=schemas.GET_CLICKY_CAPABILITIES,
        handler=tools.get_clicky_capabilities,
    )
    ctx.register_tool(
        name="observe_clicky_screen",
        toolset="clicky",
        schema=schemas.OBSERVE_CLICKY_SCREEN,
        handler=tools.observe_clicky_screen,
    )
    ctx.register_tool(
        name="explain_clicky_screen",
        toolset="clicky",
        schema=schemas.EXPLAIN_CLICKY_SCREEN,
        handler=tools.explain_clicky_screen,
    )
    ctx.register_tool(
        name="point_clicky_target",
        toolset="clicky",
        schema=schemas.POINT_CLICKY_TARGET,
        handler=tools.point_clicky_target,
    )
    ctx.register_tool(
        name="execute_clicky_action",
        toolset="clicky",
        schema=schemas.EXECUTE_CLICKY_ACTION,
        handler=tools.execute_clicky_action,
    )
