"""E2E 用例包。

导入即注册。新增用例模块时，在这里加一行 import，`run.py --list` 就能看到。
"""

from . import issue  # noqa: F401
from . import ledger  # noqa: F401
from . import nav  # noqa: F401
from . import projects  # noqa: F401
from . import settings  # noqa: F401
from . import smoke  # noqa: F401
from . import sync  # noqa: F401
from . import todo  # noqa: F401
