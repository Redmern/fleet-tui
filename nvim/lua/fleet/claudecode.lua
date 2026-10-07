local M = {}

-- fleet starts several nvims in the same second. claudecode.nvim seeds its port shuffle
-- with os.time() and probes ports with bind() before listen(), so those nvims race for one
-- port and fail with EADDRINUSE. Seed per process and retry the listen instead.
local function spread_ports()
  local utils = require('claudecode.server.utils')
  utils.shuffle_array = function(tbl)
    math.randomseed((vim.uv or vim.loop).hrtime() + (vim.uv or vim.loop).os_getpid())
    for i = #tbl, 2, -1 do
      local j = math.random(i)
      tbl[i], tbl[j] = tbl[j], tbl[i]
    end
  end

  local tcp = require('claudecode.server.tcp')
  local create_server = tcp.create_server
  tcp.create_server = function(config, callbacks, auth_token)
    local server, err
    for attempt = 1, 5 do
      server, err = create_server(config, callbacks, auth_token)
      if server then
        return server, nil
      end
      if attempt < 5 then
        vim.wait(25)
      end
    end
    return nil, err
  end
end

function M.setup()
  pcall(spread_ports)

  require('claudecode').setup({
    cwd_provider = function(ctx)
      return ctx.cwd
    end,
    terminal = {
      provider = 'native',
      show_native_term_exit_tip = false,
      split_side = 'right',
      split_width_percentage = 0.30,
    },
  })
end

return M
