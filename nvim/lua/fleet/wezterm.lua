-- WezTerm decides whether Ctrl+h/j/k/l belongs to nvim by the pane's process name, and
-- falls back to the IS_NVIM user var. fleet starts this nvim under 'fleet with-env', so the
-- process name is fleet's: announce nvim through the user var instead.
local function set_is_nvim(value)
  if #vim.api.nvim_list_uis() == 0 then
    return
  end
  vim.api.nvim_chan_send(vim.v.stderr, '\027]1337;SetUserVar=IS_NVIM=' .. value .. '\007')
end

local group = vim.api.nvim_create_augroup('fleet_wezterm', { clear = true })

vim.api.nvim_create_autocmd({ 'VimEnter', 'VimResume' }, {
  group = group,
  callback = function()
    set_is_nvim('dHJ1ZQ==')
  end,
})

vim.api.nvim_create_autocmd({ 'VimLeavePre', 'VimSuspend' }, {
  group = group,
  callback = function()
    set_is_nvim('')
  end,
})
