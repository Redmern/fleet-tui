-- fleet's own Neovim config. fleet starts its nvim panes with NVIM_APPNAME=fleet-nvim, so
-- this file lives in stdpath('config') for that app name and plugins install under
-- stdpath('data'); your own nvim config and plugin folders are never read or touched.
-- fleet rewrites this folder from its binary on 'fleet setup' and when it launches nvim:
-- edit your own config and set "Nvim config" to user instead of editing this copy.

local lazypath = vim.fn.stdpath('data') .. '/lazy/lazy.nvim'
local uv = vim.uv or vim.loop

local function lazy_ready()
  return uv.fs_stat(lazypath .. '/lua/lazy/init.lua') ~= nil
end

if not lazy_ready() then
  local out = vim.fn.system({
    'git', 'clone', '--filter=blob:none', '--branch=stable',
    'https://github.com/folke/lazy.nvim.git', lazypath,
  })
  -- Several fleet panes can start on a fresh machine at once; a clone that lost the race
  -- waits for the one that won. 'fleet setup' installs everything up front to avoid this.
  if vim.v.shell_error ~= 0 and not vim.wait(60000, lazy_ready, 200) then
    vim.api.nvim_echo({ { 'fleet: could not install lazy.nvim (needs git and network)\n', 'ErrorMsg' }, { out } }, true, {})
    return
  end
end

vim.opt.rtp:prepend(lazypath)

require('fleet.options')
require('fleet.theme').setup()

require('lazy').setup({ { import = 'fleet.plugins' } }, {
  lockfile = vim.fn.stdpath('data') .. '/lazy-lock.json',
  install = { missing = true },
  checker = { enabled = false },
  change_detection = { enabled = false },
  rocks = { enabled = false },
})

require('fleet.keymaps')
