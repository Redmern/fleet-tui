local map = vim.keymap.set

map('n', '<leader>e', '<cmd>Neotree toggle reveal<cr>', { desc = 'Toggle file tree' })
map('n', '<leader>cc', '<cmd>ClaudeCode<cr>', { desc = 'Toggle Claude Code' })
map('n', '<leader>cf', '<cmd>ClaudeCodeFocus<cr>', { desc = 'Focus Claude Code' })
map('n', '<leader>ca', '<cmd>ClaudeCodeAdd %<cr>', { desc = 'Add current file to Claude' })
map('v', '<leader>cs', ':ClaudeCodeSend<cr>', { silent = true, desc = 'Send selection to Claude' })

for _, key in ipairs({ 'h', 'j', 'k', 'l' }) do
  map('n', '<C-' .. key .. '>', '<C-w>' .. key, { desc = 'Window ' .. key })
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
