local map = vim.keymap.set

map('n', '<leader>e', '<cmd>Neotree toggle reveal<cr>', { desc = 'Toggle file tree' })
map('n', '<leader>cc', '<cmd>ClaudeCode<cr>', { desc = 'Toggle Claude Code' })
map('n', '<leader>cf', '<cmd>ClaudeCodeFocus<cr>', { desc = 'Focus Claude Code' })
map('n', '<leader>ca', '<cmd>ClaudeCodeAdd %<cr>', { desc = 'Add current file to Claude' })
map('v', '<leader>cs', ':ClaudeCodeSend<cr>', { silent = true, desc = 'Send selection to Claude' })

-- Ctrl+h/j/k/l (windows), Alt+h/j/k/l (resize) and Alt+n (leave Claude's terminal mode) come
-- from fleet's keybinds: fleet writes keybinds.generated.lua next to this file when it installs
-- this config. Without it (an older fleet), fall back to plain window moves.
local generated = loadfile(vim.fn.stdpath('config') .. '/lua/fleet/keybinds.generated.lua')
local ok, keys = pcall(generated or error)
if ok and type(keys) == 'table' and keys.setup then
  keys.setup()
else
  for _, key in ipairs({ 'h', 'j', 'k', 'l' }) do
    map('n', '<C-' .. key .. '>', '<C-w>' .. key, { desc = 'Window ' .. key })
  end
end

-- neo-tree keeps its width when the editor window next to it closes.
vim.api.nvim_create_autocmd({ 'FileType', 'BufWinEnter', 'WinEnter' }, {
  callback = function()
    local win = vim.api.nvim_get_current_win()
    if vim.bo[vim.api.nvim_win_get_buf(win)].filetype == 'neo-tree' then
      vim.wo[win].winfixwidth = true
    end
  end,
})
