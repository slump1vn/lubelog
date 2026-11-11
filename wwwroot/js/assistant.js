function loadAssistant() {
    $.get('/Ai/Assistant', function (data) {
        if (data) {
            $("#assistant-tab-pane").html(data);
            $("#assistantInput").focus();
        }
    });
}
function appendAssistantMessage(text, fromUser) {
    var bubble = $('<div class="d-flex mb-2"></div>').addClass(fromUser ? 'justify-content-end' : 'justify-content-start');
    var body = $('<div class="rounded px-3 py-2" style="max-width:85%; white-space:pre-wrap;"></div>').addClass(fromUser ? 'bg-primary text-white' : 'bg-body-secondary');
    body.text(text);
    bubble.append(body);
    $("#assistantMessages").append(bubble);
    scrollAssistantToBottom();
    return bubble;
}
function scrollAssistantToBottom() {
    var container = document.getElementById('assistantMessages');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
}
function updateAssistantDraftFields(sender) {
    var card = $(sender).closest('.assistant-draft');
    var intent = parseInt(card.find('.draft-intent').val());
    //1 service, 2 fuel, 3 expense, 4 odometer
    card.find('.draft-field-mileage').toggle(intent != 3);
    card.find('.draft-field-fuel, .draft-field-full').toggle(intent == 2);
    card.find('.draft-field-cost').toggle(intent != 4);
    card.find('.draft-field-description, .draft-field-tags').toggle(intent == 1 || intent == 3);
}
function appendAssistantDraft(response) {
    var template = document.getElementById('assistantDraftTemplate');
    var card = $(template.content.cloneNode(true)).children().first();
    var draft = response.draft;
    card.data('notes', draft.notes);
    card.find('.draft-intent').val(draft.intent);
    card.find('.draft-vehicle').val(draft.vehicleId);
    card.find('.draft-date').val(draft.date);
    card.find('.draft-mileage').val(draft.mileage > 0 ? draft.mileage : '');
    card.find('.draft-fuel').val(draft.fuelAmount > 0 ? draft.fuelAmount : '');
    card.find('.draft-cost').val(draft.cost > 0 ? draft.cost : '');
    card.find('.draft-description').val(draft.description);
    card.find('.draft-tags').val((draft.tags || []).join(' '));
    card.find('.draft-full').prop('checked', draft.isFillToFull);
    card.data('files', draft.files || []);
    if ((draft.files || []).length > 0) {
        card.find('.draft-field-attach').css('display', 'block');
        card.find('.draft-attach').prop('checked', draft.attachFiles);
    }
    var warnings = card.find('.assistant-warnings');
    (response.warnings || []).forEach(function (warning) {
        warnings.append($('<div></div>').text(warning).prepend('<i class="bi bi-exclamation-triangle me-1"></i>'));
    });
    $("#assistantMessages").append(card);
    updateAssistantDraftFields(card.find('.draft-intent'));
    scrollAssistantToBottom();
    return card;
}
function sendAssistantMessage() {
    var input = $("#assistantInput");
    var message = input.val().trim();
    if (message == '') {
        return;
    }
    appendAssistantMessage(message, true);
    input.val('');
    setAssistantBusy(true);
    var pending = appendAssistantMessage('…', false);
    $.post('/Ai/Chat', { message: message }, function (data) {
        pending.remove();
        if (data.isDraft) {
            appendAssistantDraft(data);
        } else {
            appendAssistantMessage(data.reply, false);
        }
    }).fail(function () {
        pending.remove();
        errorToast(genericErrorMessage());
    }).always(function () {
        setAssistantBusy(false);
        input.focus();
    });
}
function cancelAssistantDraft(sender) {
    var card = $(sender).closest('.assistant-draft');
    card.find('input, select').prop('disabled', true);
    card.find('.draft-actions').remove();
    card.find('.draft-cancelled').show();
}
function saveAssistantDraft(sender) {
    var card = $(sender).closest('.assistant-draft');
    var vehicleSelect = card.find('.draft-vehicle');
    var vehicleId = parseInt(vehicleSelect.val());
    if (!vehicleId) {
        vehicleSelect.addClass('is-invalid').focus();
        return;
    }
    vehicleSelect.removeClass('is-invalid');
    var draft = {
        intent: parseInt(card.find('.draft-intent').val()),
        vehicleId: vehicleId,
        date: card.find('.draft-date').val(),
        mileage: parseInt(card.find('.draft-mileage').val()) || 0,
        fuelAmount: parseFloat(card.find('.draft-fuel').val()) || 0,
        cost: parseFloat(card.find('.draft-cost').val()) || 0,
        description: card.find('.draft-description').val(),
        tags: card.find('.draft-tags').val().split(/[\s,]+/).filter(x => x != ''),
        isFillToFull: card.find('.draft-full').is(':checked'),
        notes: card.data('notes') || '',
        files: card.data('files') || [],
        attachFiles: card.find('.draft-attach').is(':checked')
    };
    $(sender).prop('disabled', true);
    $.ajax({
        url: '/Ai/Confirm',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(draft),
        success: function (data) {
            if (data.success) {
                card.find('input, select').prop('disabled', true);
                card.find('.draft-actions').remove();
                card.find('.draft-status').show();
            } else {
                $(sender).prop('disabled', false);
                errorToast(data.message);
            }
        },
        error: function () {
            $(sender).prop('disabled', false);
            errorToast(genericErrorMessage());
        }
    });
}

//shrinks a photo in the browser so uploads are small and the home AI reads it faster.
function resizeAssistantImage(file, maxSide) {
    return new Promise(function (resolve, reject) {
        var image = new Image();
        var url = URL.createObjectURL(file);
        image.onload = function () {
            var scale = Math.min(1, maxSide / Math.max(image.width, image.height));
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(image.width * scale);
            canvas.height = Math.round(image.height * scale);
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            URL.revokeObjectURL(url);
            canvas.toBlob(function (blob) { blob ? resolve(blob) : reject(); }, 'image/jpeg', 0.88);
        };
        image.onerror = function () { URL.revokeObjectURL(url); reject(); };
        image.src = url;
    });
}
function setAssistantBusy(busy) {
    $("#assistantSend, #assistantImageButton").prop('disabled', busy);
}
function sendAssistantImage(sender) {
    var file = sender.files[0];
    sender.value = '';
    if (!file) {
        return;
    }
    var preview = $('<div class="d-flex mb-2 justify-content-end"></div>');
    var previewUrl = URL.createObjectURL(file);
    preview.append($('<img class="rounded border" style="max-width:45%; max-height:240px;">').attr('src', previewUrl).attr('alt', file.name));
    $("#assistantMessages").append(preview);
    var pending = appendAssistantMessage('…', false);
    setAssistantBusy(true);
    var fail = function () {
        pending.remove();
        setAssistantBusy(false);
        errorToast(genericErrorMessage());
    };
    resizeAssistantImage(file, 1600).then(function (blob) {
        var formData = new FormData();
        formData.append('file', blob, 'photo.jpg');
        $.ajax({
            url: '/Files/HandleFileUpload', type: 'POST', data: formData, processData: false, contentType: false,
            success: function (location) {
                $.post('/Ai/Image', { location: location, name: file.name.replace(/\.[^.]+$/, '') + '.jpg' }, function (data) {
                    pending.remove();
                    setAssistantBusy(false);
                    if (data.reply) {
                        appendAssistantMessage(data.reply, false);
                    }
                    var matchCard = null;
                    if (data.payment && data.matches && data.matches.length > 0) {
                        matchCard = appendAssistantMatches(data);
                    }
                    if (data.isDraft) {
                        var draftCard = appendAssistantDraft(data);
                        if (matchCard) {
                            matchCard.data('draftCard', draftCard);
                        }
                    }
                }).fail(fail);
            },
            error: fail
        });
    }, fail);
}
function appendAssistantMatches(response) {
    var template = document.getElementById('assistantMatchTemplate');
    var card = $(template.content.cloneNode(true)).children().first();
    var payment = response.payment;
    card.find('.match-summary').text([payment.amountText, payment.date, payment.bank, payment.recipient, payment.content].filter(x => x).join(' · '));
    var list = card.find('.match-list');
    response.matches.forEach(function (match) {
        var row = $('<div class="list-group-item d-flex justify-content-between align-items-center gap-2"></div>');
        row.append($('<span class="small"></span>').text(`${match.vehicleName} - ${match.description} (${match.date}, ${match.costText})`));
        var button = $('<button type="button" class="btn btn-sm btn-primary text-nowrap"></button>').text($('#assistantMarkPaidLabel').text());
        button.on('click', function () { attachAssistantPayment(card, match, payment, response.draft.files || [], button); });
        row.append(button);
        list.append(row);
    });
    $("#assistantMessages").append(card);
    scrollAssistantToBottom();
    return card;
}
function attachAssistantPayment(card, match, payment, files, button) {
    button.prop('disabled', true);
    var draftCard = card.data('draftCard');
    $.ajax({
        url: '/Ai/AttachPayment',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ recordType: match.recordType, recordId: match.recordId, payment: payment, files: files, attachFiles: draftCard ? draftCard.find('.draft-attach').is(':checked') : false }),
        success: function (data) {
            if (data.success) {
                card.find('button').remove();
                card.find('.match-hint').remove();
                card.find('.match-status').show();
                if (draftCard) {
                    draftCard.remove();
                }
            } else {
                button.prop('disabled', false);
                errorToast(data.message);
            }
        },
        error: function () {
            button.prop('disabled', false);
            errorToast(genericErrorMessage());
        }
    });
}
