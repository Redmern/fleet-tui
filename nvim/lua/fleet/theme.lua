-- Colours from fleet's active theme. fleet writes palette.lua next to init.lua whenever the
-- theme changes ('fleet theme set', the fleet menu); this module turns it into highlight groups
-- and watches the file, so open fleet nvim panes follow the theme without a restart.

local M = {}

local uv = vim.uv or vim.loop
local palette_path = vim.fn.stdpath('config') .. '/palette.lua'

local function load_palette()
  local chunk = loadfile(palette_path)
  if not chunk then return nil end
  local ok, p = pcall(chunk)
  if ok and type(p) == 'table' and p.base and p.text then return p end
  return nil
end

local function mix(a, b, amount)
  local function ch(hex, i) return tonumber(hex:sub(i, i + 1), 16) end
  local out = '#'
  for _, i in ipairs({ 2, 4, 6 }) do
    out = out .. string.format('%02x', math.floor(ch(a, i) + (ch(b, i) - ch(a, i)) * amount + 0.5))
  end
  return out
end

local function groups(p)
  local a, b = p.ansi or {}, p.brights or {}
  local magenta, cyan = a[6] or p.lavender, a[7] or p.blue
  return {
    Normal = { fg = p.text, bg = p.base },
    NormalNC = { fg = p.text, bg = p.base },
    NormalFloat = { fg = p.text, bg = p.mantle },
    FloatBorder = { fg = p.surface1, bg = p.mantle },
    FloatTitle = { fg = p.lavender, bg = p.mantle, bold = true },
    Cursor = { fg = p.base, bg = p.cursor },
    CursorLine = { bg = mix(p.base, p.surface0, 0.6) },
    CursorColumn = { bg = mix(p.base, p.surface0, 0.6) },
    ColorColumn = { bg = p.surface0 },
    LineNr = { fg = p.surface1 },
    CursorLineNr = { fg = p.lavender, bold = true },
    SignColumn = { bg = p.base },
    FoldColumn = { fg = p.overlay0, bg = p.base },
    Folded = { fg = p.subtext0, bg = p.surface0 },
    EndOfBuffer = { fg = p.base },
    NonText = { fg = p.surface1 },
    Whitespace = { fg = p.surface1 },
    WinSeparator = { fg = p.surface1 },
    VertSplit = { fg = p.surface1 },
    StatusLine = { fg = p.text, bg = p.mantle },
    StatusLineNC = { fg = p.overlay0, bg = p.crust },
    TabLine = { fg = p.overlay0, bg = p.mantle },
    TabLineFill = { bg = p.crust },
    TabLineSel = { fg = p.text, bg = p.surface0, bold = true },
    WinBar = { fg = p.subtext0, bg = p.base },
    WinBarNC = { fg = p.overlay0, bg = p.base },
    Visual = { bg = p.surface1 },
    Search = { fg = p.base, bg = p.yellow },
    IncSearch = { fg = p.base, bg = p.lavender },
    CurSearch = { fg = p.base, bg = p.lavender },
    MatchParen = { fg = p.lavender, bg = p.surface1, bold = true },
    Pmenu = { fg = p.text, bg = p.mantle },
    PmenuSel = { fg = p.text, bg = p.surface1, bold = true },
    PmenuSbar = { bg = p.surface0 },
    PmenuThumb = { bg = p.overlay0 },
    Title = { fg = p.blue, bold = true },
    Directory = { fg = p.blue },
    Question = { fg = p.blue },
    MoreMsg = { fg = p.blue },
    ModeMsg = { fg = p.text, bold = true },
    ErrorMsg = { fg = p.red, bold = true },
    WarningMsg = { fg = p.yellow },

    Comment = { fg = p.overlay0, italic = true },
    Constant = { fg = b[2] or p.red },
    String = { fg = p.green },
    Character = { fg = p.green },
    Number = { fg = b[4] or p.yellow },
    Boolean = { fg = b[4] or p.yellow },
    Identifier = { fg = p.text },
    Function = { fg = p.blue },
    Statement = { fg = magenta },
    Keyword = { fg = magenta },
    Conditional = { fg = magenta },
    Repeat = { fg = magenta },
    Operator = { fg = cyan },
    PreProc = { fg = p.yellow },
    Type = { fg = p.yellow },
    Special = { fg = cyan },
    Delimiter = { fg = p.subtext0 },
    Underlined = { fg = p.blue, underline = true },
    Error = { fg = p.red },
    Todo = { fg = p.base, bg = p.yellow, bold = true },

    DiagnosticError = { fg = p.red },
    DiagnosticWarn = { fg = p.yellow },
    DiagnosticInfo = { fg = p.blue },
    DiagnosticHint = { fg = cyan },
    DiagnosticOk = { fg = p.green },
    DiagnosticUnderlineError = { sp = p.red, undercurl = true },
    DiagnosticUnderlineWarn = { sp = p.yellow, undercurl = true },
    DiagnosticUnderlineInfo = { sp = p.blue, undercurl = true },
    DiagnosticUnderlineHint = { sp = cyan, undercurl = true },

    DiffAdd = { bg = mix(p.base, p.green, 0.2) },
    DiffChange = { bg = mix(p.base, p.blue, 0.15) },
    DiffDelete = { bg = mix(p.base, p.red, 0.2) },
    DiffText = { bg = mix(p.base, p.blue, 0.35) },
    Added = { fg = p.green },
    Changed = { fg = p.yellow },
    Removed = { fg = p.red },

    NeoTreeNormal = { fg = p.text, bg = p.mantle },
    NeoTreeNormalNC = { fg = p.text, bg = p.mantle },
    NeoTreeEndOfBuffer = { fg = p.mantle, bg = p.mantle },
    NeoTreeWinSeparator = { fg = p.base, bg = p.base },
    NeoTreeDirectoryName = { fg = p.blue },
    NeoTreeDirectoryIcon = { fg = p.blue },
    NeoTreeRootName = { fg = p.lavender, bold = true },
    NeoTreeGitModified = { fg = p.yellow },
    NeoTreeGitAdded = { fg = p.green },
    NeoTreeGitDeleted = { fg = p.red },
    NeoTreeGitUntracked = { fg = magenta },
  }
end

function M.apply()
  local p = load_palette()
  if not p then return false end

  vim.o.background = p.light and 'light' or 'dark'
  vim.cmd('highlight clear')
  if vim.fn.exists('syntax_on') == 1 then vim.cmd('syntax reset') end
  vim.g.colors_name = 'fleet'

  for name, spec in pairs(groups(p)) do
    vim.api.nvim_set_hl(0, name, spec)
  end

  for i = 0, 7 do
    vim.g['terminal_color_' .. i] = (p.ansi or {})[i + 1]
    vim.g['terminal_color_' .. (i + 8)] = (p.brights or {})[i + 1]
  end

  pcall(vim.api.nvim_exec_autocmds, 'ColorScheme', { pattern = 'fleet', modeline = false })

  return true
end

local watcher

local function watch()
  if watcher or not uv.new_fs_event then return end
  watcher = uv.new_fs_event()
  if not watcher then return end
  -- Watch the folder, not the file: fleet replaces palette.lua atomically (write + rename),
  -- which a watch on the old file would not see.
  local ok = pcall(function()
    watcher:start(vim.fn.stdpath('config'), {}, function(err, filename)
      if err or (filename and filename ~= 'palette.lua') then return end
      vim.schedule(function() pcall(M.apply) end)
    end)
  end)
  if not ok then watcher = nil end
end

function M.setup()
  M.apply()
  watch()
end

return M
